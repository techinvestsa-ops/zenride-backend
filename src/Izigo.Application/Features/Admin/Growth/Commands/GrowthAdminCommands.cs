using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Izigo.Application.Features.Admin.Growth.Commands;

public record GrowthResult(bool Success, string? ErrorCode, object? Data = null);

// ── POST /admin/coupons ───────────────────────────────────────────────────────

public record CreateCouponCommand(
    string Code, string Title, string DiscountType, long Value,
    long MaxDiscount, long MinOrder,
    string[] Verticals, string[] Zones,
    bool FirstTripOnly, int PerUserLimit, int TotalCap,
    DateTime? StartsAt, DateTime? ExpiresAt,
    string Market, string StaffId, string StaffName) : IRequest<GrowthResult>;

public class CreateCouponHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<CreateCouponCommand, GrowthResult>
{
    public async Task<GrowthResult> Handle(CreateCouponCommand cmd, CancellationToken ct)
    {
        if (cmd.DiscountType is not ("percent" or "fixed"))
            return new(false, "INVALID_DISCOUNT_TYPE");

        var exists = await db.Coupons.AnyAsync(c => c.Code == cmd.Code && c.Market == cmd.Market, ct);
        if (exists) return new(false, "COUPON_CODE_TAKEN");

        var coupon = new Coupon
        {
            Code          = cmd.Code.ToUpperInvariant(),
            Title         = cmd.Title,
            DiscountType  = cmd.DiscountType,
            Value         = cmd.Value,
            MaxDiscount   = cmd.MaxDiscount,
            MinOrder      = cmd.MinOrder,
            VerticalsJson = JsonSerializer.Serialize(cmd.Verticals),
            ZonesJson     = JsonSerializer.Serialize(cmd.Zones),
            FirstTripOnly = cmd.FirstTripOnly,
            PerUserLimit  = cmd.PerUserLimit,
            TotalCap      = cmd.TotalCap,
            StartsAt      = cmd.StartsAt,
            ExpiresAt     = cmd.ExpiresAt,
            Market        = cmd.Market,
            IsActive      = true
        };
        db.Coupons.Add(coupon);

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.CouponChange,
            "Coupon", coupon.Id, reason: "Created",
            after: new { coupon.Code, coupon.DiscountType, coupon.Value }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null, new { id = coupon.Id, code = coupon.Code });
    }
}

// ── PATCH /admin/coupons/{code} ───────────────────────────────────────────────
// Cannot retroactively change discount_type or value if any redemptions exist.

public record UpdateCouponCommand(
    string Code, string Market,
    string? Title, string? DiscountType, long? Value, long? MaxDiscount, long? MinOrder,
    string[]? Verticals, string[]? Zones,
    bool? FirstTripOnly, int? PerUserLimit, int? TotalCap,
    DateTime? StartsAt, DateTime? ExpiresAt, bool? IsActive,
    string StaffId, string StaffName) : IRequest<GrowthResult>;

public class UpdateCouponHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UpdateCouponCommand, GrowthResult>
{
    public async Task<GrowthResult> Handle(UpdateCouponCommand cmd, CancellationToken ct)
    {
        var coupon = await db.Coupons.FirstOrDefaultAsync(
            c => c.Code == cmd.Code && c.Market == cmd.Market, ct);
        if (coupon is null) return new(false, "COUPON_NOT_FOUND");

        // Cannot change discount fields once any redemption has happened
        if (coupon.RedemptionCount > 0 &&
            (cmd.DiscountType is not null || cmd.Value.HasValue || cmd.MaxDiscount.HasValue))
            return new(false, "CANNOT_CHANGE_DISCOUNT_AFTER_REDEMPTIONS");

        var before = new
        {
            coupon.Title, coupon.DiscountType, coupon.Value, coupon.IsActive,
            coupon.ExpiresAt, coupon.TotalCap
        };

        if (cmd.Title is not null)        coupon.Title        = cmd.Title;
        if (cmd.DiscountType is not null) coupon.DiscountType = cmd.DiscountType;
        if (cmd.Value.HasValue)           coupon.Value        = cmd.Value.Value;
        if (cmd.MaxDiscount.HasValue)     coupon.MaxDiscount  = cmd.MaxDiscount.Value;
        if (cmd.MinOrder.HasValue)        coupon.MinOrder     = cmd.MinOrder.Value;
        if (cmd.Verticals is not null)    coupon.VerticalsJson = JsonSerializer.Serialize(cmd.Verticals);
        if (cmd.Zones is not null)        coupon.ZonesJson    = JsonSerializer.Serialize(cmd.Zones);
        if (cmd.FirstTripOnly.HasValue)   coupon.FirstTripOnly = cmd.FirstTripOnly.Value;
        if (cmd.PerUserLimit.HasValue)    coupon.PerUserLimit = cmd.PerUserLimit.Value;
        if (cmd.TotalCap.HasValue)        coupon.TotalCap     = cmd.TotalCap.Value;
        if (cmd.StartsAt.HasValue)        coupon.StartsAt     = cmd.StartsAt.Value;
        if (cmd.ExpiresAt.HasValue)       coupon.ExpiresAt    = cmd.ExpiresAt.Value;
        if (cmd.IsActive.HasValue)        coupon.IsActive     = cmd.IsActive.Value;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.CouponChange,
            "Coupon", coupon.Id, reason: "Updated",
            before: before, after: new { coupon.Title, coupon.IsActive, coupon.ExpiresAt }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── PUT /admin/referrals/config ───────────────────────────────────────────────
// Reward per side, qualification rule, cap. Stored as JSON on PlatformConfig.

public record UpdateReferralConfigCommand(
    long RewardReferrer, long RewardReferred,
    int QualificationTrips, long? Cap,
    string Market, string StaffId, string StaffName) : IRequest<GrowthResult>;

public class UpdateReferralConfigHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UpdateReferralConfigCommand, GrowthResult>
{
    public async Task<GrowthResult> Handle(UpdateReferralConfigCommand cmd, CancellationToken ct)
    {
        var config = await db.PlatformConfigs.FirstOrDefaultAsync(c => c.Market == cmd.Market, ct);
        if (config is null) return new(false, "CONFIG_NOT_FOUND");

        var before = new { config.ReferralConfigJson };

        var newConfig = new
        {
            reward_referrer      = cmd.RewardReferrer,
            reward_referred      = cmd.RewardReferred,
            qualification_trips  = cmd.QualificationTrips,
            cap                  = cmd.Cap
        };
        config.ReferralConfigJson = JsonSerializer.Serialize(newConfig);

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.ReferralConfigChange,
            "PlatformConfig", config.Id, reason: "Referral config updated",
            before: before, after: newConfig, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null, newConfig);
    }
}

// ── POST /admin/incentives ────────────────────────────────────────────────────
// audience, condition (trips/hours/zone/time window), reward, budget cap.

public record CreateIncentiveCommand(
    string Title, string Audience,
    object Condition, long Reward, string Currency,
    long? BudgetCap, DateTime? StartsAt, DateTime? EndsAt,
    string Market, string StaffId, string StaffName) : IRequest<GrowthResult>;

public class CreateIncentiveHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<CreateIncentiveCommand, GrowthResult>
{
    public async Task<GrowthResult> Handle(CreateIncentiveCommand cmd, CancellationToken ct)
    {
        var incentive = new Incentive
        {
            Title         = cmd.Title,
            Audience      = cmd.Audience,
            ConditionJson = JsonSerializer.Serialize(cmd.Condition),
            Reward        = cmd.Reward,
            Currency      = cmd.Currency,
            BudgetCap     = cmd.BudgetCap,
            StartsAt      = cmd.StartsAt,
            EndsAt        = cmd.EndsAt,
            Market        = cmd.Market,
            IsActive      = true
        };
        db.Incentives.Add(incentive);

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.IncentiveChange,
            "Incentive", incentive.Id, reason: "Created",
            after: new { incentive.Title, incentive.Audience, incentive.Reward }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null, new { id = incentive.Id });
    }
}
