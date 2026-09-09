namespace Izigo.Application.Features.Payments.Dtos;

// ── Payment Methods ───────────────────────────────────────────────────────────

public record PaymentMethodDto(
    string Id,
    string Type,       // cash | wallet | card | orange_money | moov_money | wave | mtn_momo
    string Label,      // "Visa •••• 4242" | "+225 07 •• •• 42"
    bool IsDefault,
    bool IsVerified
);

public record AddPaymentMethodRequest(
    string Type,
    string? Phone,       // for mobile money
    string? CardToken,   // tokenised by gateway SDK on client
    string? Last4
);

// ── Payment Intent ────────────────────────────────────────────────────────────

public record CreatePaymentIntentRequest(
    string Purpose,        // topup | trip | package
    string? ReferenceId,   // trip_id or package_id
    long Amount,
    string Currency,
    string Method,
    string? Phone,
    string? IdempotencyKey
);

public record PaymentIntentDto(
    string PaymentId,
    string Status,
    string? CheckoutUrl,
    string? UssdCode,
    string? DeepLink,
    bool OtpRequired
);

// ── Payment record ────────────────────────────────────────────────────────────

public record PaymentDto(
    string PaymentId,
    string Purpose,
    string? ReferenceId,
    long Amount,
    string Currency,
    string Method,
    string Status,
    DateTime CreatedAt
);
