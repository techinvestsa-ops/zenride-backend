using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Promotions.Dtos;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Promotions.Queries;

// ── GET /coupons ──────────────────────────────────────────────────────────────

public record GetCouponsQuery(string UserId) : IRequest<List<CouponDto>>;

public class GetCouponsHandler(IApplicationDbContext db)
    : IRequestHandler<GetCouponsQuery, List<CouponDto>>
{
    public async Task<List<CouponDto>> Handle(GetCouponsQuery req, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var coupons = await db.Coupons
            .Where(c => c.IsActive &&
                        (c.StartsAt == null || c.StartsAt <= now) &&
                        (c.ExpiresAt == null || c.ExpiresAt > now) &&
                        (c.TotalCap == 0 || c.RedemptionCount < c.TotalCap))
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        return coupons.Select(c => new CouponDto(
            c.Code, c.Title, c.DiscountType, c.Value, c.MaxDiscount, c.MinOrder,
            System.Text.Json.JsonSerializer.Deserialize<string[]>(c.VerticalsJson) ?? [],
            c.FirstTripOnly, c.ExpiresAt,
            UsageLeft: c.TotalCap == 0 ? int.MaxValue : c.TotalCap - c.RedemptionCount,
            IsValid: true)).ToList();
    }
}

// ── GET /driver/incentives ────────────────────────────────────────────────────

public record GetIncentivesQuery(string DriverId) : IRequest<List<IncentiveDto>>;

public class GetIncentivesHandler(IApplicationDbContext db)
    : IRequestHandler<GetIncentivesQuery, List<IncentiveDto>>
{
    public async Task<List<IncentiveDto>> Handle(GetIncentivesQuery req, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var incentives = await db.Incentives
            .Where(i => i.IsActive &&
                        i.Audience == "driver" &&
                        (i.StartsAt == null || i.StartsAt <= now) &&
                        (i.EndsAt   == null || i.EndsAt   >  now))
            .ToListAsync(ct);

        // TODO: compute Progress / Target per driver from ConditionJson
        return incentives.Select(i => new IncentiveDto(
            i.Id, i.Title, i.Audience, i.Reward, i.Currency,
            i.ConditionJson,
            Progress: null,
            Target:   null,
            i.StartsAt, i.EndsAt, i.IsActive)).ToList();
    }
}
