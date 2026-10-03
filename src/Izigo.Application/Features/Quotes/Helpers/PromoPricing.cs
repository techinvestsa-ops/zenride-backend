using System.Text.Json;
using Izigo.Domain.Entities;

namespace Izigo.Application.Features.Quotes.Helpers;

public static class PromoPricing
{
    public static (long Discount, string? Title, string? Code) Best(
        long listTotal,
        IReadOnlyList<Coupon> coupons,
        bool riderHasCompletedTrip,
        string? requestedCode,
        string vertical,
        DateTime now)
    {
        if (listTotal <= 0 || coupons.Count == 0)
            return (0, null, null);

        var asked = requestedCode?.Trim().ToUpperInvariant();
        long bestAmount = 0;
        string? bestTitle = null;
        string? bestCode = null;

        foreach (var coupon in coupons)
        {
            if (!coupon.IsActive) continue;
            if (coupon.StartsAt.HasValue && coupon.StartsAt > now) continue;
            if (coupon.ExpiresAt.HasValue && coupon.ExpiresAt <= now) continue;
            if (coupon.TotalCap > 0 && coupon.RedemptionCount >= coupon.TotalCap) continue;
            if (listTotal < coupon.MinOrder) continue;
            if (coupon.FirstTripOnly && riderHasCompletedTrip) continue;
            if (!AllowsVertical(coupon, vertical)) continue;

            var codeMatches = asked is not null && string.Equals(coupon.Code, asked, StringComparison.OrdinalIgnoreCase);
            if (!coupon.AutoApply && !codeMatches) continue;

            var amount = DiscountOf(coupon, listTotal);
            if (amount <= 0) continue;
            if (amount < bestAmount) continue;
            if (amount == bestAmount && !codeMatches) continue;

            bestAmount = amount;
            bestTitle = string.IsNullOrWhiteSpace(coupon.Title) ? coupon.Code : coupon.Title;
            bestCode = coupon.Code;
        }

        return (bestAmount, bestTitle, bestCode);
    }

    public static long DiscountOf(Coupon coupon, long listTotal)
    {
        long amount = coupon.DiscountType == "percent"
            ? (long)Math.Round(listTotal * (coupon.Value / 100m))
            : coupon.Value;
        if (coupon.MaxDiscount > 0)
            amount = Math.Min(amount, coupon.MaxDiscount);
        return Math.Clamp(amount, 0, listTotal);
    }

    private static bool AllowsVertical(Coupon coupon, string vertical)
    {
        if (string.IsNullOrWhiteSpace(coupon.VerticalsJson) || coupon.VerticalsJson == "[]")
            return true;
        try
        {
            var verticals = JsonSerializer.Deserialize<string[]>(coupon.VerticalsJson) ?? [];
            return verticals.Length == 0 ||
                   verticals.Contains(vertical, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return true;
        }
    }
}
