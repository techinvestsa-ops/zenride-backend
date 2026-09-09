namespace Izigo.Application.Features.Driver.Dtos;

public record EarningsSummaryDto(
    long TotalEarnings,
    int TotalTrips,
    int TotalOnlineMinutes,
    long CashEarnings,
    long WalletEarnings,
    string Currency,
    string Period   // week | month | all
);

public record EarningsSeriesItemDto(string Date, long Earnings, int Trips);

public record EarningsSeriesDto(string Granularity, EarningsSeriesItemDto[] Series);

public record EarningsTripDto(
    string TripId,
    string Code,
    string Vertical,
    long DriverEarnings,
    string PaymentMethod,
    DateTime? CompletedAt
);

public record DriverWalletDto(
    string WalletId,
    long AvailableBalance,
    long PendingCashSettlement,
    long AdjustmentsTotal,
    string Currency,
    bool InstantWithdrawalEnabled,
    long MinWithdrawal,
    long WithdrawalFee
);

public record DriverWalletTransactionDto(
    string TransactionId,
    string Type,
    long Amount,
    long BalanceAfter,
    string Status,
    string? ReferenceId,
    string? Reason,
    DateTime CreatedAt
);

public record CashSettlementDto(
    long Outstanding,
    long Cap,
    bool IsBlocked,
    string Currency
);

public record PayoutMethodDto(
    string Id,
    string Method,
    string? AccountName,
    string? AccountNumber,
    string? BankCode,
    string? MobilePhone,
    bool IsDefault,
    bool IsVerified
);

// ── Requests ──────────────────────────────────────────────────────────────────

public record WithdrawEarningsRequest(long Amount, string PayoutMethodId, string? IdempotencyKey);

public record AddPayoutMethodRequest(
    string Method,       // bank | orange_money | wave | moov_money | mtn_momo
    string? BankCode,
    string? AccountNumber,
    string? AccountName,
    string? MobilePhone
);

public record PayCashSettlementRequest(long Amount, string? Reference);

public record DisputeWalletRequest(string Description, string? TripId, long? DisputedAmount);
