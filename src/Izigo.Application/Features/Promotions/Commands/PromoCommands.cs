using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Promotions.Dtos;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Promotions.Commands;

// ── POST /coupons/validate ────────────────────────────────────────────────────

public record ValidateCouponCommand(string UserId, ValidateCouponRequest Request)
    : IRequest<CouponValidationDto>;

public class ValidateCouponHandler(IApplicationDbContext db)
    : IRequestHandler<ValidateCouponCommand, CouponValidationDto>
{
    public async Task<CouponValidationDto> Handle(ValidateCouponCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;
        var now = DateTime.UtcNow;

        // Resolve the order amount from the quote (spec: server-side validation)
        var quote = await db.Quotes.FirstOrDefaultAsync(q => q.Id == req.QuoteId && q.RiderId == cmd.UserId, ct);
        if (quote == null)
            return Invalid("QUOTE_NOT_FOUND", "Quote not found or has expired.");

        var options = System.Text.Json.JsonSerializer
            .Deserialize<Izigo.Application.Features.Quotes.Dtos.QuoteOptionDto[]>(quote.OptionsJson) ?? [];
        var orderAmount = options.FirstOrDefault()?.Fare.Total ?? 0;

        var coupon = await db.Coupons
            .FirstOrDefaultAsync(c => c.Code == req.Code.ToUpper() && c.IsActive, ct);

        if (coupon == null)
            return Invalid("COUPON_NOT_FOUND", "Coupon not found.");

        if (coupon.ExpiresAt.HasValue && coupon.ExpiresAt <= now)
            return Invalid("COUPON_EXPIRED", "This coupon has expired.");

        if (coupon.StartsAt.HasValue && coupon.StartsAt > now)
            return Invalid("COUPON_NOT_STARTED", "This coupon is not yet active.");

        if (coupon.TotalCap > 0 && coupon.RedemptionCount >= coupon.TotalCap)
            return Invalid("COUPON_EXHAUSTED", "This coupon has reached its usage limit.");

        if (orderAmount < coupon.MinOrder)
            return Invalid("ORDER_TOO_SMALL",
                $"Minimum order amount is {coupon.MinOrder} to use this coupon.");

        var verticals = System.Text.Json.JsonSerializer
            .Deserialize<string[]>(coupon.VerticalsJson) ?? [];
        if (verticals.Length > 0 && !verticals.Contains(req.Vertical, StringComparer.OrdinalIgnoreCase))
            return Invalid("VERTICAL_NOT_ELIGIBLE",
                $"This coupon is not valid for {req.Vertical} orders.");

        if (coupon.FirstTripOnly)
        {
            var hasTrips = await db.Trips.AnyAsync(t => t.RiderId == cmd.UserId &&
                                                        t.JobState == JobState.Completed, ct);
            if (hasTrips)
                return Invalid("FIRST_TRIP_ONLY", "This coupon is valid for first trips only.");
        }

        long discount = coupon.DiscountType == "percent"
            ? Math.Min(orderAmount * coupon.Value / 100, coupon.MaxDiscount)
            : Math.Min(coupon.Value, coupon.MaxDiscount);

        return new CouponValidationDto(
            IsValid:       true,
            Code:          coupon.Code,
            DiscountType:  coupon.DiscountType,
            DiscountValue: discount,
            NewTotal:      Math.Max(0, orderAmount - discount),
            MaxDiscount:   coupon.MaxDiscount,
            MinOrder:      coupon.MinOrder,
            ErrorCode:     null,
            ErrorMessage:  null);
    }

    private static CouponValidationDto Invalid(string code, string message) =>
        new(false, null, "", 0, 0, 0, 0, code, message);
}

// ── POST /referrals/redeem ────────────────────────────────────────────────────

public record RedeemReferralCommand(string UserId, RedeemReferralRequest Request)
    : IRequest<ReferralRedeemDto>;

public class RedeemReferralHandler(IApplicationDbContext db)
    : IRequestHandler<RedeemReferralCommand, ReferralRedeemDto>
{
    public async Task<ReferralRedeemDto> Handle(RedeemReferralCommand cmd, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == cmd.UserId, ct)
            ?? throw new KeyNotFoundException("User not found.");

        if (user.ReferredByUserId != null)
            throw new InvalidOperationException("CONFLICT: Referral code already applied.");

        var referrer = await db.Users
            .FirstOrDefaultAsync(u => u.ReferralCode == cmd.Request.ReferralCode.ToUpper(), ct)
            ?? throw new KeyNotFoundException("Invalid referral code.");

        if (referrer.Id == cmd.UserId)
            throw new ArgumentException("VALIDATION_ERROR: Cannot use your own referral code.");

        user.ReferredByUserId = referrer.Id;

        // Credit bonus to new user's wallet
        const long BonusAmount = 1_000;
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == cmd.UserId, ct);
        if (wallet == null)
        {
            wallet = new Wallet { UserId = cmd.UserId };
            db.Wallets.Add(wallet);
        }

        wallet.Balance += BonusAmount;
        db.WalletTransactions.Add(new WalletTransaction
        {
            WalletId     = wallet.Id,
            Type         = WalletTransactionType.Bonus,
            Amount       = BonusAmount,
            BalanceAfter = wallet.Balance,
            Title        = "Referral bonus",
            Subtitle     = $"From {referrer.FirstName}",
            Status       = PaymentStatus.Succeeded,
        });

        await db.SaveChangesAsync(ct);

        return new ReferralRedeemDto(true, BonusAmount, "XOF",
            $"You received {BonusAmount} XOF for joining with a referral code.");
    }
}
