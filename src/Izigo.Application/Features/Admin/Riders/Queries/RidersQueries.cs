using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Application.Features.Admin.Ops.Queries;
using Izigo.Application.Features.Admin.Trips.Queries;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Riders.Queries;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record RiderRowDto(
    string Id, string Name, string PhoneMasked, string? Zone,
    int TotalTrips, long LifetimeSpend, long WalletBalance, string Currency,
    decimal Rating, int Referrals, string Status, bool IsFlagged);

public record RiderCountersDto(int TotalTrips, int CompletedTrips, int CancelledTrips,
    long TotalSpend, string Currency, decimal Rating, int TotalRatings, int Referrals);

public record RiderFlagDto(bool IsFlagged, string? Reason, string? Severity);

public record RiderDetailDto(
    string Id, string Name, string PhoneMasked, string? Email,
    string? PhotoUrl, string Language, DateTime JoinedAt, string Status,
    RiderCountersDto Counters, IEnumerable<TripListRowDto> RecentTrips,
    IEnumerable<object> WalletLedger, IEnumerable<object> Devices,
    RiderFlagDto Flag, IEnumerable<object> Referrals);

public record RiderDeviceDto(string DeviceId, string? Model, string? OsVersion, string Platform,
    string? AppVersion, DateTime LastSeenAt, bool HasPushToken, bool IsDuplicateDevice);

// ── GET /admin/riders ─────────────────────────────────────────────────────────
// Filters: status=active|flagged|suspended, zone, min_trips, has_wallet_balance
// Fields: name, phone, zone, trips, lifetime spend, wallet, rating, referrals, status

public record GetAdminRidersQuery(
    string Market, string? Status, string? Zone,
    int? MinTrips, bool? HasWalletBalance, string? Q,
    int Page, int PerPage) : IRequest<object>;

public class GetAdminRidersHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminRidersQuery, object>
{
    public async Task<object> Handle(GetAdminRidersQuery req, CancellationToken ct)
    {
        var usersQuery = db.Users.Where(u => u.Role == UserRole.Rider).AsQueryable();

        // Status filter: active | flagged | suspended
        if (!string.IsNullOrWhiteSpace(req.Status))
        {
            usersQuery = req.Status.ToLower() switch
            {
                "active"    => usersQuery.Where(u => u.Status == UserStatus.Active && !u.IsFlagged),
                "flagged"   => usersQuery.Where(u => u.IsFlagged),
                "suspended" => usersQuery.Where(u => u.Status == UserStatus.Suspended),
                "blocked"   => usersQuery.Where(u => u.Status == UserStatus.Blocked),
                _           => usersQuery
            };
        }

        if (!string.IsNullOrWhiteSpace(req.Q))
            usersQuery = usersQuery.Where(u =>
                u.FirstName.Contains(req.Q) || u.LastName.Contains(req.Q) ||
                u.Phone.Contains(req.Q) || (u.Email != null && u.Email.Contains(req.Q)));

        var perPage  = Math.Max(1, Math.Min(100, req.PerPage));
        var allUsers = await usersQuery
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Phone, u.Status,
                               u.Rating, u.IsFlagged, u.CreatedAt })
            .ToListAsync(ct);

        var userIds = allUsers.Select(u => u.Id).ToList();

        // Trip aggregates
        var tripAggs = await db.Trips
            .Where(t => t.Market == req.Market && userIds.Contains(t.RiderId)
                     && t.JobState == JobState.Completed)
            .GroupBy(t => t.RiderId)
            .Select(g => new { RiderId = g.Key, Count = g.Count(), Spend = g.Sum(t => (long)t.FareGross) })
            .ToListAsync(ct);
        var tripMap = tripAggs.ToDictionary(g => g.RiderId);

        // Wallet balances
        var wallets = await db.Wallets
            .Where(w => userIds.Contains(w.UserId))
            .Select(w => new { w.UserId, w.Balance, w.Currency })
            .ToListAsync(ct);
        var walletMap = wallets.ToDictionary(w => w.UserId);

        // Referral counts
        var referralCounts = await db.Users
            .Where(u => u.ReferredByUserId != null && userIds.Contains(u.ReferredByUserId))
            .GroupBy(u => u.ReferredByUserId!)
            .Select(g => new { ReferrerId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var referralMap = referralCounts.ToDictionary(r => r.ReferrerId, r => r.Count);

        // Apply min_trips and has_wallet_balance filters (post-aggregation)
        var rows = allUsers.Select(u =>
        {
            tripMap.TryGetValue(u.Id, out var trip);
            walletMap.TryGetValue(u.Id, out var wallet);
            return new
            {
                User    = u,
                Trips   = trip?.Count ?? 0,
                Spend   = trip?.Spend ?? 0L,
                Balance = wallet?.Balance ?? 0L,
                Currency = wallet?.Currency ?? "XOF"
            };
        }).ToList();

        if (req.MinTrips.HasValue)
            rows = rows.Where(r => r.Trips >= req.MinTrips.Value).ToList();

        if (req.HasWalletBalance == true)
            rows = rows.Where(r => r.Balance > 0).ToList();

        var total    = rows.Count;
        var lastPage = (int)Math.Ceiling((double)total / perPage);
        var paged    = rows.Skip((req.Page - 1) * perPage).Take(perPage).ToList();

        // Facets by status
        var facets = new Dictionary<string, Dictionary<string, int>>
        {
            ["status"] = new()
            {
                ["active"]    = allUsers.Count(u => u.Status == UserStatus.Active && !u.IsFlagged),
                ["flagged"]   = allUsers.Count(u => u.IsFlagged),
                ["suspended"] = allUsers.Count(u => u.Status == UserStatus.Suspended),
            }
        };

        var result = paged.Select(r => new RiderRowDto(
            r.User.Id,
            $"{r.User.FirstName} {r.User.LastName}".Trim(),
            GetLiveOpsHandler.MaskPhone(r.User.Phone),
            null, // zone requires geospatial lookup
            r.Trips, r.Spend,
            r.Balance, r.Currency,
            r.User.Rating,
            referralMap.GetValueOrDefault(r.User.Id, 0),
            r.User.IsFlagged ? "flagged" : r.User.Status.ToString().ToLower(),
            r.User.IsFlagged)).ToArray();

        return AdminApiResponse.Ok(result, new AdminPagedMeta(req.Page, perPage, total, lastPage, facets));
    }
}

// ── GET /admin/riders/{id} ────────────────────────────────────────────────────

public record GetAdminRiderDetailQuery(string RiderId) : IRequest<RiderDetailDto?>;

public class GetAdminRiderDetailHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminRiderDetailQuery, RiderDetailDto?>
{
    public async Task<RiderDetailDto?> Handle(GetAdminRiderDetailQuery req, CancellationToken ct)
    {
        var user = await db.Users
            .Where(u => u.Id == req.RiderId && u.Role == UserRole.Rider)
            .Select(u => new
            {
                u.Id, u.FirstName, u.LastName, u.Phone, u.Email, u.PhotoUrl,
                u.Language, u.Status, u.Rating, u.TotalRatings,
                u.IsFlagged, u.FlagReason, u.FlagSeverity, u.CreatedAt
            })
            .FirstOrDefaultAsync(ct);

        if (user is null) return null;

        // Trip counters
        var tripStats = await db.Trips
            .Where(t => t.RiderId == req.RiderId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total     = g.Count(),
                Completed = g.Count(t => t.JobState == JobState.Completed),
                Cancelled = g.Count(t => t.JobState == JobState.CancelledByRider
                                      || t.JobState == JobState.CancelledByAdmin),
                Spend     = g.Where(t => t.JobState == JobState.Completed).Sum(t => (long)t.FareGross)
            })
            .FirstOrDefaultAsync(ct);

        var referralCount = await db.Users.CountAsync(u => u.ReferredByUserId == req.RiderId, ct);

        // Recent trips (last 5)
        var recentTrips = await db.Trips
            .Where(t => t.RiderId == req.RiderId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(5)
            .Select(t => new
            {
                t.Id, t.Code, t.Vertical, t.JobState, t.RiderId, t.DriverId,
                t.PickupLabel, t.DropoffLabel, t.DistanceM,
                t.FareGross, t.Currency, t.PaymentMethod, t.CreatedAt
            })
            .ToListAsync(ct);

        var recentTripDtos = recentTrips.Select(t => new TripListRowDto(
            t.Id, t.Code, t.Vertical.ToString().ToLower(),
            new TripListRiderDto(t.RiderId, user.FirstName + " " + user.LastName),
            null,
            $"{t.PickupLabel} → {t.DropoffLabel}",
            t.DistanceM.HasValue ? Math.Round(t.DistanceM.Value / 1000m, 1) : null,
            t.FareGross, t.Currency,
            t.PaymentMethod.ToString().ToLower(),
            GetLiveOpsHandler.ToDisplayState(t.JobState),
            t.CreatedAt)).ToArray();

        // Wallet ledger (last 20)
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == req.RiderId, ct);
        var ledger = new List<object>();
        if (wallet is not null)
        {
            var txns = await db.WalletTransactions
                .Where(tx => tx.WalletId == wallet.Id)
                .OrderByDescending(tx => tx.CreatedAt)
                .Take(20)
                .Select(tx => new
                {
                    tx.Id, Type = tx.Type.ToString(), tx.Amount, tx.BalanceAfter,
                    tx.Title, tx.Subtitle, tx.CreatedAt
                })
                .ToListAsync(ct);
            ledger = txns.Cast<object>().ToList();
        }

        // Devices
        var devices = await db.UserDevices
            .Where(d => d.UserId == req.RiderId)
            .Select(d => new { d.DeviceId, d.Model, d.OsVersion, d.Platform, d.AppVersion, d.LastActiveAt, d.FcmToken })
            .ToListAsync(ct);

        // Duplicate device detection: check if device_id appears on other users
        var deviceIds = devices.Select(d => d.DeviceId).ToList();
        var duplicateDeviceIds = await db.UserDevices
            .Where(d => deviceIds.Contains(d.DeviceId) && d.UserId != req.RiderId)
            .Select(d => d.DeviceId)
            .Distinct()
            .ToListAsync(ct);
        var dupSet = duplicateDeviceIds.ToHashSet();

        var deviceDtos = devices.Select(d => (object)new
        {
            d.DeviceId, d.Model, d.OsVersion, d.Platform, d.AppVersion,
            LastSeenAt = d.LastActiveAt, HasPushToken = d.FcmToken != null,
            IsDuplicateDevice = dupSet.Contains(d.DeviceId)
        }).ToList();

        // Referral tree (users referred by this rider)
        var referrals = await db.Users
            .Where(u => u.ReferredByUserId == req.RiderId)
            .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName, JoinedAt = u.CreatedAt })
            .ToListAsync(ct);

        var currency = wallet?.Currency ?? "XOF";

        return new RiderDetailDto(
            user.Id,
            $"{user.FirstName} {user.LastName}".Trim(),
            GetLiveOpsHandler.MaskPhone(user.Phone),
            user.Email,
            user.PhotoUrl,
            user.Language,
            user.CreatedAt,
            user.Status.ToString().ToLower(),
            new RiderCountersDto(
                tripStats?.Total ?? 0,
                tripStats?.Completed ?? 0,
                tripStats?.Cancelled ?? 0,
                tripStats?.Spend ?? 0,
                currency,
                user.Rating, user.TotalRatings,
                referralCount),
            recentTripDtos,
            ledger,
            deviceDtos,
            new RiderFlagDto(user.IsFlagged, user.FlagReason, user.FlagSeverity),
            referrals.Cast<object>());
    }
}

// ── GET /admin/riders/{id}/trips ──────────────────────────────────────────────
// Same shape as /admin/trips — reuses GetAdminTripsQuery with rider_id filter

public record GetRiderTripsQuery(string RiderId, string Market, int Page, int PerPage)
    : IRequest<object>;

public class GetRiderTripsHandler(IMediator mediator) : IRequestHandler<GetRiderTripsQuery, object>
{
    public Task<object> Handle(GetRiderTripsQuery req, CancellationToken ct)
        => mediator.Send(new GetAdminTripsQuery(
            req.Market, null, null, null, null, null,
            req.RiderId, null, null, null, null, null, req.Page, req.PerPage), ct);
}

// ── GET /admin/riders/{id}/devices ────────────────────────────────────────────

public record GetRiderDevicesQuery(string RiderId) : IRequest<object?>;

public class GetRiderDevicesHandler(IApplicationDbContext db)
    : IRequestHandler<GetRiderDevicesQuery, object?>
{
    public async Task<object?> Handle(GetRiderDevicesQuery req, CancellationToken ct)
    {
        var exists = await db.Users.AnyAsync(u => u.Id == req.RiderId, ct);
        if (!exists) return null;

        var devices = await db.UserDevices
            .Where(d => d.UserId == req.RiderId)
            .Select(d => new { d.DeviceId, d.Model, d.OsVersion, d.Platform, d.AppVersion,
                               d.LastActiveAt, d.FcmToken, d.LastIpAddress })
            .ToListAsync(ct);

        var deviceIds = devices.Select(d => d.DeviceId).ToList();
        var dupIds    = await db.UserDevices
            .Where(d => deviceIds.Contains(d.DeviceId) && d.UserId != req.RiderId)
            .Select(d => d.DeviceId).Distinct().ToListAsync(ct);
        var dupSet = dupIds.ToHashSet();

        var result = devices.Select(d => new RiderDeviceDto(
            d.DeviceId, d.Model, d.OsVersion, d.Platform, d.AppVersion,
            d.LastActiveAt, d.FcmToken != null, dupSet.Contains(d.DeviceId))).ToArray();

        return AdminApiResponse.Ok(result, new AdminPagedMeta(1, result.Length, result.Length, 1));
    }
}
