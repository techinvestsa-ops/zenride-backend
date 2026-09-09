using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Growth.Queries;

// ── A15: PROMOTIONS, REFERRALS & INCENTIVES ───────────────────────────────────

// ── GET /admin/coupons ────────────────────────────────────────────────────────
// Returns redemptions, cost_to_date, cap, status, expiry.

public record CouponRowDto(string Id, string Code, string Title, string DiscountType,
    long Value, long MaxDiscount, long MinOrder, int TotalCap, int RedemptionCount,
    long CostToDate, DateTime? StartsAt, DateTime? ExpiresAt, bool IsActive, string Market);

public record GetAdminCouponsQuery(string Market, string? Status, int Page, int PerPage)
    : IRequest<object>;

public class GetAdminCouponsHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminCouponsQuery, object>
{
    public async Task<object> Handle(GetAdminCouponsQuery req, CancellationToken ct)
    {
        var query = db.Coupons.Where(c => c.Market == req.Market).AsQueryable();

        if (req.Status == "active")   query = query.Where(c => c.IsActive && (c.ExpiresAt == null || c.ExpiresAt > DateTime.UtcNow));
        if (req.Status == "inactive") query = query.Where(c => !c.IsActive);
        if (req.Status == "expired")  query = query.Where(c => c.ExpiresAt != null && c.ExpiresAt <= DateTime.UtcNow);

        var perPage  = Math.Max(1, Math.Min(100, req.PerPage));
        var total    = await query.CountAsync(ct);
        var lastPage = (int)Math.Ceiling((double)total / perPage);

        var coupons = await query
            .OrderByDescending(c => c.CreatedAt)
            .Skip((req.Page - 1) * perPage).Take(perPage)
            .ToListAsync(ct);

        // cost_to_date = sum of wallet bonus transactions referencing each coupon
        var codes = coupons.Select(c => c.Code).ToList();

        // Derive cost from trips that used each coupon (via Quote.PromoCode → Trip.FareDiscount)
        var quotesByCode = await db.Quotes
            .Where(q => q.PromoCode != null && codes.Contains(q.PromoCode!))
            .Select(q => new { q.Id, q.PromoCode })
            .ToListAsync(ct);

        var quoteIdToCode = quotesByCode.ToDictionary(q => q.Id, q => q.PromoCode!);
        var quoteIds = quotesByCode.Select(q => q.Id).ToList();

        var tripDiscounts = await db.Trips
            .Where(t => t.QuoteId != null && quoteIds.Contains(t.QuoteId!) &&
                        t.JobState == JobState.Completed)
            .Select(t => new { t.QuoteId, t.FareDiscount })
            .ToListAsync(ct);

        var costByCode = tripDiscounts
            .GroupBy(t => quoteIdToCode.TryGetValue(t.QuoteId!, out var code) ? code : "")
            .Where(g => g.Key != "")
            .ToDictionary(g => g.Key, g => g.Sum(t => (long)t.FareDiscount));

        var rows = coupons.Select(c => new CouponRowDto(
            c.Id, c.Code, c.Title, c.DiscountType, c.Value, c.MaxDiscount, c.MinOrder,
            c.TotalCap, c.RedemptionCount,
            costByCode.TryGetValue(c.Code, out var cost) ? cost : 0L,
            c.StartsAt, c.ExpiresAt, c.IsActive, c.Market)).ToArray();

        var facets = new Dictionary<string, Dictionary<string, int>>
        {
            ["status"] = new()
            {
                ["active"]   = coupons.Count(c => c.IsActive && (c.ExpiresAt == null || c.ExpiresAt > DateTime.UtcNow)),
                ["inactive"] = coupons.Count(c => !c.IsActive),
                ["expired"]  = coupons.Count(c => c.ExpiresAt != null && c.ExpiresAt <= DateTime.UtcNow)
            }
        };

        return AdminApiResponse.Ok(rows, new AdminPagedMeta(req.Page, perPage, total, lastPage, facets));
    }
}

// ── GET /admin/coupons/{code}/performance ─────────────────────────────────────
// Trips driven, new riders acquired, cost per acquisition, repeat rate after use.

public record CouponPerformanceDto(string Code, int TripsDriven, int NewRidersAcquired,
    long CostToDate, decimal CostPerAcquisition, decimal RepeatRatePct);

public record GetCouponPerformanceQuery(string Code, string Market) : IRequest<CouponPerformanceDto?>;

public class GetCouponPerformanceHandler(IApplicationDbContext db)
    : IRequestHandler<GetCouponPerformanceQuery, CouponPerformanceDto?>
{
    public async Task<CouponPerformanceDto?> Handle(GetCouponPerformanceQuery req, CancellationToken ct)
    {
        var coupon = await db.Coupons.FirstOrDefaultAsync(
            c => c.Code == req.Code && c.Market == req.Market, ct);
        if (coupon is null) return null;

        // Trips that used this coupon (via Quote.PromoCode)
        var quoteIds = await db.Quotes
            .Where(q => q.PromoCode == req.Code)
            .Select(q => q.Id)
            .ToListAsync(ct);

        var trips = await db.Trips
            .Where(t => t.QuoteId != null && quoteIds.Contains(t.QuoteId!) &&
                        t.JobState == JobState.Completed)
            .Select(t => new { t.RiderId, t.FareDiscount, t.CreatedAt })
            .ToListAsync(ct);

        var tripsDriven = trips.Count;
        var costToDate  = trips.Sum(t => (long)t.FareDiscount);

        var riderIds = trips.Select(t => t.RiderId).Distinct().ToList();

        // New riders acquired: those whose earliest completed trip used this coupon
        var earliestTripPerRider = await db.Trips
            .Where(t => riderIds.Contains(t.RiderId) && t.JobState == JobState.Completed)
            .GroupBy(t => t.RiderId)
            .Select(g => new { RiderId = g.Key, FirstAt = g.Min(t => t.CreatedAt) })
            .ToDictionaryAsync(x => x.RiderId, x => x.FirstAt, ct);

        var couponTripByRider = trips.ToDictionary(t => t.RiderId, t => t.CreatedAt);
        var newRidersAcquired = riderIds.Count(rid =>
            earliestTripPerRider.TryGetValue(rid, out var firstAt) &&
            couponTripByRider.TryGetValue(rid, out var couponAt) &&
            Math.Abs((firstAt - couponAt).TotalSeconds) < 5);

        var costPerAcquisition = newRidersAcquired > 0
            ? Math.Round((decimal)costToDate / newRidersAcquired, 2)
            : 0m;

        // Repeat rate: % of coupon users who completed another trip within 30 days
        var cutoff     = DateTime.UtcNow;
        var riderIdsArr = riderIds.ToArray();
        var repeatCount = 0;
        if (riderIdsArr.Length > 0)
        {
            foreach (var riderId in riderIdsArr)
            {
                if (!couponTripByRider.TryGetValue(riderId, out var couponAt)) continue;
                var deadline = couponAt.AddDays(30);
                var hasRepeat = await db.Trips.AnyAsync(t =>
                    t.RiderId == riderId &&
                    t.JobState == JobState.Completed &&
                    t.CreatedAt > couponAt &&
                    t.CreatedAt <= deadline, ct);
                if (hasRepeat) repeatCount++;
            }
        }

        var repeatRatePct = riderIds.Count > 0
            ? Math.Round((decimal)repeatCount / riderIds.Count * 100, 1)
            : 0m;

        return new CouponPerformanceDto(
            req.Code, tripsDriven, newRidersAcquired,
            costToDate, costPerAcquisition, repeatRatePct);
    }
}

// ── GET /admin/referrals ──────────────────────────────────────────────────────
// Signups, qualified referrals, payouts, suspicious clusters (same device, same IP).

public record GetAdminReferralsQuery(string Market, int Page, int PerPage) : IRequest<object>;

public class GetAdminReferralsHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminReferralsQuery, object>
{
    public async Task<object> Handle(GetAdminReferralsQuery req, CancellationToken ct)
    {
        // Read referral qualification trips from platform config
        var config = await db.PlatformConfigs.FirstOrDefaultAsync(c => c.Market == req.Market, ct);
        var qualificationTrips = 1; // default; overridable from ReferralConfigJson

        var referredUsers = await db.Users
            .Where(u => u.ReferredByUserId != null)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.ReferredByUserId,
                               u.CreatedAt, u.Status })
            .ToListAsync(ct);

        var referrerIds   = referredUsers.Select(u => u.ReferredByUserId!).Distinct().ToList();
        var referredIds   = referredUsers.Select(u => u.Id).ToList();
        var allUserIds    = referrerIds.Concat(referredIds).Distinct().ToList();

        // Trip counts per referred user to determine qualification
        var tripCounts = await db.Trips
            .Where(t => referredIds.Contains(t.RiderId) && t.JobState == JobState.Completed)
            .GroupBy(t => t.RiderId)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.UserId, x => x.Count, ct);

        // Referral bonus payouts (wallet bonuses whose title references referral)
        var payoutsTotal = await db.WalletTransactions
            .Where(wt => wt.Type == WalletTransactionType.Bonus &&
                         wt.Title.Contains("referral"))
            .SumAsync(wt => (long?)wt.Amount, ct) ?? 0L;

        // Suspicious clusters: same device or same IP across referrer+referred pairs
        var devices = await db.UserDevices
            .Where(d => allUserIds.Contains(d.UserId))
            .Select(d => new { d.UserId, d.DeviceId, d.LastIpAddress })
            .ToListAsync(ct);

        var suspiciousByDevice = devices
            .GroupBy(d => d.DeviceId)
            .Where(g => g.Select(d => d.UserId).Distinct().Count() > 1)
            .Select(g => new
            {
                device_id  = g.Key,
                user_count = g.Select(d => d.UserId).Distinct().Count(),
                user_ids   = g.Select(d => d.UserId).Distinct().ToList()
            })
            .Cast<object>().ToList();

        var suspiciousByIp = devices
            .Where(d => d.LastIpAddress != null)
            .GroupBy(d => d.LastIpAddress!)
            .Where(g => g.Select(d => d.UserId).Distinct().Count() > 1)
            .Select(g => new
            {
                ip         = g.Key,
                user_count = g.Select(d => d.UserId).Distinct().Count(),
                user_ids   = g.Select(d => d.UserId).Distinct().ToList()
            })
            .Cast<object>().ToList();

        var perPage  = Math.Max(1, Math.Min(100, req.PerPage));
        var total    = referredUsers.Count;
        var lastPage = (int)Math.Ceiling((double)total / perPage);

        var rows = referredUsers
            .Skip((req.Page - 1) * perPage).Take(perPage)
            .Select(u => new
            {
                referred_user_id   = u.Id,
                referred_user_name = $"{u.FirstName} {u.LastName}".Trim(),
                referrer_user_id   = u.ReferredByUserId,
                signed_up_at       = u.CreatedAt,
                status             = u.Status.ToString().ToLower(),
                trips_completed    = tripCounts.TryGetValue(u.Id, out var tc) ? tc : 0,
                qualified          = tripCounts.TryGetValue(u.Id, out var tc2) && tc2 >= qualificationTrips
            })
            .ToList();

        return new
        {
            success = true,
            data = new
            {
                referrals             = rows,
                signups               = total,
                qualified_count       = referredUsers.Count(u => tripCounts.TryGetValue(u.Id, out var tc) && tc >= qualificationTrips),
                total_payouts_amount  = payoutsTotal,
                suspicious_clusters   = new { by_device = suspiciousByDevice, by_ip = suspiciousByIp }
            },
            meta = new AdminPagedMeta(req.Page, perPage, total, lastPage)
        };
    }
}

// ── GET /admin/incentives ─────────────────────────────────────────────────────
// Driver quests and bonus programmes with enrolment, progress, and cost.

public record IncentiveRowDto(string Id, string Title, string Audience, bool IsActive,
    long Reward, string Currency, long? BudgetCap, long BudgetUsed,
    DateTime? StartsAt, DateTime? EndsAt, string Market);

public record GetAdminIncentivesQuery(string Market, string? Status, int Page, int PerPage)
    : IRequest<object>;

public class GetAdminIncentivesHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminIncentivesQuery, object>
{
    public async Task<object> Handle(GetAdminIncentivesQuery req, CancellationToken ct)
    {
        var query = db.Incentives.Where(i => i.Market == req.Market).AsQueryable();

        if (req.Status == "active")   query = query.Where(i => i.IsActive);
        if (req.Status == "inactive") query = query.Where(i => !i.IsActive);

        var perPage  = Math.Max(1, Math.Min(100, req.PerPage));
        var total    = await query.CountAsync(ct);
        var lastPage = (int)Math.Ceiling((double)total / perPage);

        var incentives = await query
            .OrderByDescending(i => i.CreatedAt)
            .Skip((req.Page - 1) * perPage).Take(perPage)
            .ToListAsync(ct);

        var rows = incentives.Select(i => new IncentiveRowDto(
            i.Id, i.Title, i.Audience, i.IsActive,
            i.Reward, i.Currency, i.BudgetCap, i.BudgetUsed,
            i.StartsAt, i.EndsAt, i.Market)).ToArray();

        var facets = new Dictionary<string, Dictionary<string, int>>
        {
            ["status"] = new()
            {
                ["active"]   = incentives.Count(i => i.IsActive),
                ["inactive"] = incentives.Count(i => !i.IsActive)
            }
        };

        return AdminApiResponse.Ok(rows, new AdminPagedMeta(req.Page, perPage, total, lastPage, facets));
    }
}
