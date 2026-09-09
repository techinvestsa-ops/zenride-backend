using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Application.Features.Admin.Ops.Queries;
using Izigo.Application.Features.Admin.Trips.Queries;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Drivers.Queries;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record DriverRowDto(
    string Id, string Name, string PhoneMasked, string KycStatus,
    bool IsOnline, int TotalTrips, long LifetimeEarnings, long CashOwed,
    long WalletBalance, string Currency, decimal Rating,
    int? DocsExpiringDays, string Status);

public record DriverVehicleDto(string? Make, string? Model, int? Year, string? Color,
    string? Plate, string? Type, int Seats);

public record DriverPerformanceDto(decimal Rating, decimal AcceptanceRate,
    decimal CompletionRate, decimal CancellationRate, int TotalTrips);

public record DriverMoneyDto(long WalletBalance, long CashOwed, long LifetimeEarnings,
    string Currency, string? PayoutAccountMasked, string? PayoutMethod);

public record DriverDocumentDto(string Id, string Type, string Status,
    DateTime? ExpiresAt, DateTime? ReviewedAt);

public record DriverDetailDto(
    string Id, string Name, string PhoneMasked, string? Email, string? PhotoUrl,
    DateTime JoinedAt, string KycStatus, bool IsOnline,
    DriverVehicleDto? Vehicle, IEnumerable<string> VerticalsAllowed,
    DriverPerformanceDto Performance, DriverMoneyDto Money,
    IEnumerable<DriverDocumentDto> Documents);

public record DriverJobRowDto(string Id, string Code, string Vertical,
    string PickupLabel, string DropoffLabel, long DriverEarnings, long Commission,
    decimal CommissionRate, string Currency, string Status, DateTime CreatedAt);

public record DriverRatingDto(int Stars, string? TripCode, DateTime RatedAt);

public record ExportDriversResult(string JobId, string StatusUrl);

// ── GET /admin/drivers ────────────────────────────────────────────────────────
// Filters: kyc_status, online, vertical, vehicle_type, zone, suspended, has_cash_owed,
// docs_expiring_within_days

public record GetAdminDriversQuery(
    string Market, string? KycStatus, bool? Online, string? Vertical,
    string? VehicleType, string? Zone, bool? Suspended, bool? HasCashOwed,
    int? DocsExpiringWithinDays, string? Q, int Page, int PerPage) : IRequest<object>;

public class GetAdminDriversHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminDriversQuery, object>
{
    public async Task<object> Handle(GetAdminDriversQuery req, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Start from DriverProfiles
        var profileQuery = db.DriverProfiles.AsQueryable();

        if (req.Online.HasValue)
            profileQuery = profileQuery.Where(d => d.IsOnline == req.Online.Value);

        if (req.Suspended == true)
            profileQuery = profileQuery.Where(d => d.User.Status == UserStatus.Suspended);

        if (!string.IsNullOrWhiteSpace(req.KycStatus) &&
            Enum.TryParse<KycStatus>(req.KycStatus, true, out var kycStatus))
            profileQuery = profileQuery.Where(d => d.KycStatus == kycStatus);

        if (!string.IsNullOrWhiteSpace(req.Q))
            profileQuery = profileQuery.Where(d =>
                d.User.FirstName.Contains(req.Q) || d.User.LastName.Contains(req.Q) ||
                d.User.Phone.Contains(req.Q));

        var profiles = await profileQuery
            .Select(d => new
            {
                d.Id, d.UserId, d.KycStatus, d.IsOnline, d.AcceptanceRate,
                d.CompletionRate, d.CancellationRate, d.TotalTripsCompleted,
                d.User.FirstName, d.User.LastName, d.User.Phone, d.User.Status
            })
            .ToListAsync(ct);

        var driverIds = profiles.Select(d => d.Id).ToList();

        // Vehicle type filter
        if (!string.IsNullOrWhiteSpace(req.VehicleType) &&
            Enum.TryParse<VehicleType>(req.VehicleType, true, out var vt))
        {
            var driverIdsWithVehicle = await db.Vehicles
                .Where(v => driverIds.Contains(v.DriverId) && v.IsActive && v.Type == vt)
                .Select(v => v.DriverId).ToListAsync(ct);
            profiles = profiles.Where(d => driverIdsWithVehicle.Contains(d.Id)).ToList();
            driverIds = profiles.Select(d => d.Id).ToList();
        }

        // Driver wallets for cash owed
        var wallets = await db.DriverWallets
            .Where(w => driverIds.Contains(w.DriverId))
            .Select(w => new { w.DriverId, w.AvailableBalance, w.PendingCashSettlement })
            .ToListAsync(ct);
        var walletMap = wallets.ToDictionary(w => w.DriverId);

        if (req.HasCashOwed == true)
        {
            var withCash = wallets.Where(w => w.PendingCashSettlement > 0)
                .Select(w => w.DriverId).ToHashSet();
            profiles = profiles.Where(d => withCash.Contains(d.Id)).ToList();
            driverIds = profiles.Select(d => d.Id).ToList();
        }

        // Docs expiring filter
        Dictionary<string, int>? docsExpiringMap = null;
        if (req.DocsExpiringWithinDays.HasValue)
        {
            var cutoff = now.AddDays(req.DocsExpiringWithinDays.Value);
            var expiringDocs = await db.DriverDocuments
                .Where(d => driverIds.Contains(d.DriverId) && d.ExpiresAt.HasValue
                         && d.ExpiresAt.Value >= now && d.ExpiresAt.Value <= cutoff
                         && d.Status == OnboardingStepStatus.Approved)
                .Select(d => new { d.DriverId, d.ExpiresAt })
                .ToListAsync(ct);

            var filteredDriverIds = expiringDocs.Select(d => d.DriverId).Distinct().ToHashSet();
            profiles = profiles.Where(d => filteredDriverIds.Contains(d.Id)).ToList();
            driverIds = profiles.Select(d => d.Id).ToList();

            docsExpiringMap = expiringDocs
                .GroupBy(d => d.DriverId)
                .ToDictionary(g => g.Key, g =>
                    (int)Math.Ceiling((g.Min(d => d.ExpiresAt!.Value) - now).TotalDays));
        }

        // Lifetime earnings from completed trips
        var earnings = await db.Trips
            .Where(t => t.Market == req.Market && driverIds.Contains(t.DriverId ?? "")
                     && t.JobState == JobState.Completed)
            .GroupBy(t => t.DriverId!)
            .Select(g => new { DriverId = g.Key, Total = g.Sum(t => (long)t.DriverEarnings) })
            .ToListAsync(ct);
        var earningsMap = earnings.ToDictionary(e => e.DriverId, e => e.Total);

        var facets = new Dictionary<string, Dictionary<string, int>>
        {
            ["kyc_status"] = profiles.GroupBy(d => d.KycStatus.ToString())
                .ToDictionary(g => g.Key.ToLower(), g => g.Count())
        };

        var perPage  = Math.Max(1, Math.Min(100, req.PerPage));
        var total    = profiles.Count;
        var lastPage = (int)Math.Ceiling((double)total / perPage);
        var paged    = profiles.Skip((req.Page - 1) * perPage).Take(perPage).ToList();

        var result = paged.Select(d =>
        {
            walletMap.TryGetValue(d.Id, out var wallet);
            return new DriverRowDto(
                d.Id,
                $"{d.FirstName} {d.LastName}".Trim(),
                GetLiveOpsHandler.MaskPhone(d.Phone),
                d.KycStatus.ToString().ToLower(),
                d.IsOnline,
                d.TotalTripsCompleted,
                earningsMap.GetValueOrDefault(d.Id, 0),
                wallet?.PendingCashSettlement ?? 0,
                wallet?.AvailableBalance ?? 0,
                "XOF",
                0m, // rating is on User.Rating via DriverProfile — can join if needed
                docsExpiringMap?.GetValueOrDefault(d.Id),
                d.Status.ToString().ToLower());
        }).ToArray();

        return AdminApiResponse.Ok(result, new AdminPagedMeta(req.Page, perPage, total, lastPage, facets));
    }
}

// ── GET /admin/drivers/{id} ───────────────────────────────────────────────────

public record GetAdminDriverDetailQuery(string DriverId) : IRequest<DriverDetailDto?>;

public class GetAdminDriverDetailHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminDriverDetailQuery, DriverDetailDto?>
{
    public async Task<DriverDetailDto?> Handle(GetAdminDriverDetailQuery req, CancellationToken ct)
    {
        var driver = await db.DriverProfiles
            .Where(d => d.Id == req.DriverId)
            .Select(d => new
            {
                DriverId = d.Id, d.KycStatus, d.IsOnline, d.VerticalsAllowed,
                d.AcceptanceRate, d.CompletionRate, d.CancellationRate, d.TotalTripsCompleted,
                UserId = d.User.Id, d.User.FirstName, d.User.LastName, d.User.Phone,
                d.User.Email, d.User.PhotoUrl, d.User.Rating, d.User.CreatedAt
            })
            .FirstOrDefaultAsync(ct);

        if (driver is null) return null;

        var vehicle = await db.Vehicles
            .Where(v => v.DriverId == req.DriverId && v.IsActive)
            .Select(v => new DriverVehicleDto(v.Make, v.Model, v.Year, v.Color, v.Plate, v.Type.ToString(), v.Seats))
            .FirstOrDefaultAsync(ct);

        var driverWallet = await db.DriverWallets
            .Where(w => w.DriverId == req.DriverId)
            .Select(w => new { w.AvailableBalance, w.PendingCashSettlement })
            .FirstOrDefaultAsync(ct);

        var lifetimeEarnings = await db.Trips
            .Where(t => t.DriverId == req.DriverId && t.JobState == JobState.Completed)
            .SumAsync(t => (long)t.DriverEarnings, ct);

        var payoutMethod = await db.PayoutMethods
            .Where(pm => pm.DriverId == req.DriverId && pm.IsDefault)
            .Select(pm => new { pm.Method, pm.AccountNumber, pm.MobilePhone })
            .FirstOrDefaultAsync(ct);

        // Mask payout account: show last 4 only
        var payoutAccountMasked = payoutMethod?.AccountNumber is not null
            ? $"•••• {payoutMethod.AccountNumber[^Math.Min(4, payoutMethod.AccountNumber.Length)..]}"
            : payoutMethod?.MobilePhone is not null
                ? GetLiveOpsHandler.MaskPhone(payoutMethod.MobilePhone)
                : null;

        var docs = await db.DriverDocuments
            .Where(d => d.DriverId == req.DriverId)
            .Select(d => new DriverDocumentDto(d.Id, d.Type.ToString(), d.Status.ToString(), d.ExpiresAt, d.ReviewedAt))
            .ToListAsync(ct);

        return new DriverDetailDto(
            req.DriverId,
            $"{driver.FirstName} {driver.LastName}".Trim(),
            GetLiveOpsHandler.MaskPhone(driver.Phone),
            driver.Email, driver.PhotoUrl,
            driver.CreatedAt,
            driver.KycStatus.ToString().ToLower(),
            driver.IsOnline,
            vehicle,
            driver.VerticalsAllowed.Select(v => v.ToString().ToLower()),
            new DriverPerformanceDto(driver.Rating, driver.AcceptanceRate,
                driver.CompletionRate, driver.CancellationRate, driver.TotalTripsCompleted),
            new DriverMoneyDto(
                driverWallet?.AvailableBalance ?? 0,
                driverWallet?.PendingCashSettlement ?? 0,
                lifetimeEarnings, "XOF",
                payoutAccountMasked, payoutMethod?.Method),
            docs);
    }
}

// ── GET /admin/drivers/{id}/jobs ──────────────────────────────────────────────

public record GetDriverJobsQuery(string DriverId, string Market, int Page, int PerPage)
    : IRequest<object?>;

public class GetDriverJobsHandler(IApplicationDbContext db)
    : IRequestHandler<GetDriverJobsQuery, object?>
{
    public async Task<object?> Handle(GetDriverJobsQuery req, CancellationToken ct)
    {
        var exists = await db.DriverProfiles.AnyAsync(d => d.Id == req.DriverId, ct);
        if (!exists) return null;

        var perPage  = Math.Max(1, Math.Min(100, req.PerPage));
        var total    = await db.Trips.CountAsync(t => t.DriverId == req.DriverId, ct);
        var lastPage = (int)Math.Ceiling((double)total / perPage);

        var jobs = await db.Trips
            .Where(t => t.DriverId == req.DriverId)
            .OrderByDescending(t => t.CreatedAt)
            .Skip((req.Page - 1) * perPage)
            .Take(perPage)
            .Select(t => new DriverJobRowDto(
                t.Id, t.Code,
                t.Vertical.ToString().ToLower(),
                t.PickupLabel, t.DropoffLabel,
                t.DriverEarnings, t.CommissionAmount, t.CommissionRate,
                t.Currency,
                GetLiveOpsHandler.ToDisplayState(t.JobState),
                t.CreatedAt))
            .ToListAsync(ct);

        return AdminApiResponse.Ok(jobs, new AdminPagedMeta(req.Page, perPage, total, lastPage));
    }
}

// ── GET /admin/drivers/{id}/ratings ───────────────────────────────────────────

public record GetDriverRatingsQuery(string DriverId, int Page, int PerPage) : IRequest<object?>;

public class GetDriverRatingsHandler(IApplicationDbContext db)
    : IRequestHandler<GetDriverRatingsQuery, object?>
{
    public async Task<object?> Handle(GetDriverRatingsQuery req, CancellationToken ct)
    {
        var exists = await db.DriverProfiles.AnyAsync(d => d.Id == req.DriverId, ct);
        if (!exists) return null;

        var perPage  = Math.Max(1, Math.Min(100, req.PerPage));
        var total    = await db.Trips.CountAsync(
            t => t.DriverId == req.DriverId && t.RatingByRider.HasValue, ct);
        var lastPage = (int)Math.Ceiling((double)total / perPage);

        var ratings = await db.Trips
            .Where(t => t.DriverId == req.DriverId && t.RatingByRider.HasValue)
            .OrderByDescending(t => t.CompletedAt)
            .Skip((req.Page - 1) * perPage)
            .Take(perPage)
            .Select(t => new DriverRatingDto(t.RatingByRider!.Value, t.Code,
                t.CompletedAt ?? t.CreatedAt))
            .ToListAsync(ct);

        return AdminApiResponse.Ok(ratings, new AdminPagedMeta(req.Page, perPage, total, lastPage));
    }
}

// ── GET /admin/drivers/export ─────────────────────────────────────────────────

public record ExportDriversQuery(string StaffId, string Market) : IRequest<ExportDriversResult>;

public class ExportDriversHandler(IApplicationDbContext db, IJobDispatcher jobDispatcher)
    : IRequestHandler<ExportDriversQuery, ExportDriversResult>
{
    public async Task<ExportDriversResult> Handle(ExportDriversQuery req, CancellationToken ct)
    {
        var job = new Domain.Entities.BackgroundJob
        {
            Type = "export", Status = "queued", InitiatedByStaffId = req.StaffId
        };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(ct);
        jobDispatcher.Enqueue(job.Id, job.Type);
        return new(job.Id, $"/api/v1/admin/jobs/{job.Id}");
    }
}
