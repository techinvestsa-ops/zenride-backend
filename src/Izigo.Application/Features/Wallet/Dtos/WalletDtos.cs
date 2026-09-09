namespace Izigo.Application.Features.Wallets.Dtos;

public record WalletDto(
    string WalletId,
    long Balance,
    long PendingIn,
    long PendingOut,
    string Currency,
    bool IsLocked,
    string? LockReason,
    long MinTopup,
    long MaxBalance
);

public record WalletTransactionDto(
    string TransactionId,
    string Type,
    long Amount,
    long BalanceAfter,
    string Title,
    string? Subtitle,
    string Status,
    string? ReferenceId,
    DateTime CreatedAt
);

// spec: ?period=week|month → per-bucket totals backing the Weekly Spending card
public record WalletSummaryDto(
    string Period,
    string Currency,
    WalletBucketDto[] Buckets
);

public record WalletBucketDto(string Label, long Amount);

public record WalletLimitsDto(
    long MinTopup,
    long MaxTopup,
    long MaxBalance,
    long MaxTransfer,
    long MaxWithdrawal,
    string Currency
);

public record WalletRecipientDto(
    string UserId,
    string Name,
    string Phone,
    string? PhotoUrl
);

// ── Requests ──────────────────────────────────────────────────────────────────

public record TopUpRequest(long Amount, string Method, string? Phone, string? IdempotencyKey);

public record TransferLookupRequest(string Phone);

public record TransferRequest(
    string RecipientUserId,
    long Amount,
    string? Note,
    string? IdempotencyKey
);

public record WithdrawRequest(
    long Amount,
    string PaymentMethodId,   // maps to a saved UserPaymentMethod
    string? IdempotencyKey
);
