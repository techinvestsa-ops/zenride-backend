using Izigo.Domain.Common;
using Izigo.Domain.Enums;

namespace Izigo.Domain.Entities;

public class Wallet : BaseEntity
{
    public Wallet() => Id = EntityId.ForWallet();

    public string UserId { get; set; } = string.Empty;
    public long Balance { get; set; }
    public long PendingIn { get; set; }
    public long PendingOut { get; set; }
    public string Currency { get; set; } = "XOF";
    public bool IsLocked { get; set; }
    public string? LockReason { get; set; }
    public long MinTopup { get; set; } = 500;
    public long MaxBalance { get; set; } = 1_000_000;

    public ICollection<WalletTransaction> Transactions { get; set; } = [];
}

public class WalletTransaction : BaseEntity
{
    public WalletTransaction() => Id = EntityId.ForWalletTransaction();
    public string WalletId { get; set; } = string.Empty;
    public WalletTransactionType Type { get; set; }
    public long Amount { get; set; }
    public long BalanceAfter { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public string? ReferenceId { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Succeeded;
    public string? Reason { get; set; }
    public string? CreatedBy { get; set; }
}

public class DriverWallet : BaseEntity
{
    public DriverWallet() => Id = EntityId.ForDriverWallet();

    public string DriverId { get; set; } = string.Empty;
    public long AvailableBalance { get; set; }
    public long PendingCashSettlement { get; set; }
    public long AdjustmentsTotal { get; set; }
    public string Currency { get; set; } = "XOF";
    public bool InstantWithdrawalEnabled { get; set; }
    public long MinWithdrawal { get; set; } = 1000;
    public long WithdrawalFee { get; set; }
    public bool IsLocked { get; set; }
    public string? LockReason { get; set; }

    public ICollection<DriverWalletTransaction> Transactions { get; set; } = [];
}

public class DriverWalletTransaction : BaseEntity
{
    public DriverWalletTransaction() => Id = EntityId.ForDriverWalletTx();
    public string DriverWalletId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public long Amount { get; set; }
    public long BalanceAfter { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Succeeded;
    public string? ReferenceId { get; set; }
    public string? Reason { get; set; }
    public string? CreatedBy { get; set; }
}

public class PayoutMethod : BaseEntity
{
    public string DriverId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string? BankCode { get; set; }
    public string? AccountNumber { get; set; }
    public string? AccountName { get; set; }
    public string? Provider { get; set; }
    public string? MobilePhone { get; set; }
    public bool IsDefault { get; set; }
    public bool IsVerified { get; set; }
}

public class Payment : BaseEntity
{
    public Payment() => Id = EntityId.ForPayment();

    public string UserId { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string? ReferenceId { get; set; }
    public long Amount { get; set; }
    public string Currency { get; set; } = "XOF";
    public PaymentMethod Method { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? Gateway { get; set; }
    public string? GatewayReference { get; set; }
    public string? FailureReason { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? CheckoutUrl { get; set; }
    public string? UssdCode { get; set; }
    public string? DeepLink { get; set; }
    public bool OtpRequired { get; set; }
    public bool IsReconciled { get; set; }
    public string? ReconciliationNote { get; set; }
    public string Market { get; set; } = string.Empty;

    public ICollection<WebhookEvent> WebhookEvents { get; set; } = [];
}

public class WebhookEvent : BaseEntity
{
    public string PaymentId { get; set; } = string.Empty;
    public string Gateway { get; set; } = string.Empty;
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string RawPayload { get; set; } = string.Empty;
    public bool IsProcessed { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

public class Payout : BaseEntity
{
    public Payout() => Id = EntityId.ForPayout();

    public string DriverId { get; set; } = string.Empty;
    public string PayoutMethodId { get; set; } = string.Empty;
    public long GrossAmount { get; set; }
    public long Fee { get; set; }
    public long NetAmount { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? ProviderReference { get; set; }
    public string? FailureReason { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? RejectionReason { get; set; }
    public string Currency { get; set; } = "XOF";
    public string Market { get; set; } = string.Empty;

    public ICollection<PayoutAttempt> Attempts { get; set; } = [];
}

public class PayoutAttempt : BaseEntity
{
    public string PayoutId { get; set; } = string.Empty;
    public PaymentStatus Status { get; set; }
    public string? ProviderReference { get; set; }
    public string? FailureReason { get; set; }
}

public class FareRule : BaseEntity
{
    public FareRule() => Id = EntityId.ForFareRule();

    public ServiceClass ServiceClass { get; set; }
    public long Base { get; set; }
    public long PerKm { get; set; }
    public long PerMin { get; set; }
    public long Minimum { get; set; }
    public long WaitingPerMin { get; set; }
    public long CancellationFee { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; } = 1;
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
    public string? UpdatedByStaffId { get; set; }
    public string Market { get; set; } = string.Empty;
}

public class CommissionConfig : BaseEntity
{
    public decimal CommissionRate { get; set; } = 0.20m;
    public decimal BonusRate { get; set; } = 0.035m;
    public string BonusLabel { get; set; } = "ChopMonie Bonus";
    public long CashSettlementCap { get; set; } = 10_000;
    public string VerticalOverridesJson { get; set; } = "{}";
    public int Version { get; set; } = 1;
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;
    public string? UpdatedByStaffId { get; set; }
    public string Market { get; set; } = string.Empty;
}

/// <summary>Rider-saved payment method (tokenised card or mobile money account).</summary>
public class UserPaymentMethod : BaseEntity
{
    public UserPaymentMethod() => Id = EntityId.ForUserPaymentMethod();

    public string UserId   { get; set; } = string.Empty;
    public string Type     { get; set; } = string.Empty;   // card | orange_money | moov_money | wave | mtn_momo
    public string Label    { get; set; } = string.Empty;   // "Visa •••• 4242"
    public string? Last4   { get; set; }
    public string? Phone   { get; set; }
    public string? GatewayToken { get; set; }
    public bool IsDefault  { get; set; }
    public bool IsVerified { get; set; }
}

public class SurgeZone : BaseEntity
{
    public string ZoneId { get; set; } = string.Empty;
    public decimal Multiplier { get; set; } = 1.0m;
    public bool IsAutomatic { get; set; } = true;
    public bool IsActive { get; set; }
    public string? Reason { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? OverriddenByStaffId { get; set; }
}
