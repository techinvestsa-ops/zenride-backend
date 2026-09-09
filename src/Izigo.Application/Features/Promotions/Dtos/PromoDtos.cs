namespace Izigo.Application.Features.Promotions.Dtos;

public record CouponDto(
    string Code,
    string Title,
    string DiscountType,    // percent | fixed
    long Value,
    long MaxDiscount,
    long MinOrder,
    string[] Verticals,
    bool FirstTripOnly,
    DateTime? ExpiresAt,
    int UsageLeft,          // spec: usage_left
    bool IsValid
);

public record CouponValidationDto(
    bool IsValid,
    string? Code,
    string DiscountType,
    long DiscountValue,
    long NewTotal,          // spec: new_total
    long MaxDiscount,
    long MinOrder,
    string? ErrorCode,
    string? ErrorMessage
);

public record ReferralRedeemDto(
    bool Success,
    long BonusAmount,
    string Currency,
    string Message
);

public record IncentiveDto(
    string Id,
    string Title,
    string Audience,
    long Reward,
    string Currency,
    string ConditionSummary,
    int? Progress,           // spec: progress / target / reward
    int? Target,
    DateTime? StartsAt,
    DateTime? EndsAt,
    bool IsActive
);

// Requests
public record ValidateCouponRequest(
    string Code,
    string Vertical,
    string QuoteId      // spec: quote_id — server looks up fare from the quote
);

public record RedeemReferralRequest(string ReferralCode);
