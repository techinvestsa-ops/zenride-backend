using Izigo.Domain.Common;
using Izigo.Domain.Enums;

namespace Izigo.Domain.Entities;

public class Zone : BaseEntity
{
    public Zone() => Id = EntityId.ForZone();
    public string Name { get; set; } = string.Empty;
    public string Market { get; set; } = string.Empty;
    public string PolygonGeoJson { get; set; } = string.Empty;
    public decimal CenterLat { get; set; }
    public decimal CenterLng { get; set; }
    public string Status { get; set; } = "planned"; // planned | pilot | live
    public List<Vertical> VerticalsEnabled { get; set; } = [];
}

public class Notification : BaseEntity
{
    public Notification() => Id = EntityId.ForNotification();
    public string UserId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? DeepLink { get; set; }
    public bool IsRead { get; set; }
    public NotificationGroup Group { get; set; }
}

public class Conversation : BaseEntity
{
    public Conversation() => Id = EntityId.ForConversation();
    public ConversationKind Kind { get; set; }
    public string? TripId { get; set; }
    public string? SupportTicketId { get; set; }
    public bool IsClosed { get; set; }
    public DateTime? ClosedAt { get; set; }

    public ICollection<Message> Messages { get; set; } = [];
}

public class Message : BaseEntity
{
    public Message() => Id = EntityId.ForMessage();
    public string ConversationId { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string SenderRole { get; set; } = string.Empty; // rider | driver | support | system
    public MessageType Type { get; set; }
    public string? Body { get; set; }
    public string? MediaUrl { get; set; }
    public int? DurationS { get; set; }
    public string? LocalId { get; set; }              // client-supplied, echoed back
    public DateTime? ReadAt { get; set; }
}

public class SupportTicket : BaseEntity
{
    public SupportTicket() => Id = EntityId.ForTicket();
    public string UserId { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? TripId { get; set; }
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;
    public string Reference { get; set; } = string.Empty;
    public string? AssignedStaffId { get; set; }
    public DateTime? SlaDeadline { get; set; }
    public string? ResolutionCode { get; set; }
    public string Market { get; set; } = string.Empty;
}

public class SupportTicketMessage : BaseEntity
{
    public SupportTicketMessage() => Id = EntityId.ForSupportTicketMessage();
    public string TicketId { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string SenderType { get; set; } = string.Empty; // user | staff
    public string Body { get; set; } = string.Empty;
    public bool IsInternalNote { get; set; }
    public string? AttachmentsJson { get; set; }
}

public class SosIncident : BaseEntity
{
    public SosIncident() => Id = EntityId.ForSosIncident();
    public string? TripId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty; // rider | driver
    public SosType Type { get; set; }
    public SosStatus Status { get; set; } = SosStatus.Active;
    public decimal Lat { get; set; }
    public decimal Lng { get; set; }
    public string? AcknowledgedByStaffId { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public string? Outcome { get; set; }  // resolved | false_alarm | escalated
    public string? Notes { get; set; }
    public string? AuthorityName { get; set; }
    public string? AuthorityReference { get; set; }
    public string Market { get; set; } = string.Empty;
}

public class Coupon : BaseEntity
{
    public Coupon() => Id = EntityId.ForCoupon();
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string DiscountType { get; set; } = string.Empty; // percent | fixed
    public long Value { get; set; }
    public long MaxDiscount { get; set; }
    public long MinOrder { get; set; }
    public string VerticalsJson { get; set; } = "[]";
    public string ZonesJson { get; set; } = "[]";
    public bool FirstTripOnly { get; set; }
    public int PerUserLimit { get; set; } = 1;
    public int TotalCap { get; set; }
    public int RedemptionCount { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string Market { get; set; } = string.Empty;
}

public class Broadcast : BaseEntity
{
    public Broadcast() => Id = EntityId.ForBroadcast();
    public string Audience { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;  // push | sms | in_app
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? DeepLink { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? SentAt { get; set; }
    public int DeliveredCount { get; set; }
    public int FailedCount { get; set; }
    public decimal OpenedPct { get; set; }
    public bool IsCancelled { get; set; }
    public string? SentByStaffId { get; set; }
    public string Market { get; set; } = string.Empty;
}

// Admin-specific
public class Staff : AuditableEntity
{
    public Staff() => Id = EntityId.ForStaff();
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string PasswordHash { get; set; } = string.Empty;
    public string RoleKey { get; set; } = string.Empty;
    public StaffStatus Status { get; set; } = StaffStatus.Active;
    public List<string> Markets { get; set; } = [];
    public bool TwoFaEnabled { get; set; }
    public string? TwoFaSecret { get; set; }
    public bool MustChangePassword { get; set; } = true;
    public string? SuspensionReason { get; set; }
    public DateTime? SuspendedUntil { get; set; }

    // Permission overrides on top of the role
    public List<string> GrantedPermissions { get; set; } = [];
    public List<string> RevokedPermissions { get; set; } = [];

    public DateTime? LastActiveAt { get; set; }
    public ICollection<StaffRefreshToken> RefreshTokens { get; set; } = [];
}

public class StaffRefreshToken : BaseEntity
{
    public StaffRefreshToken() => Id = EntityId.ForStaffRefreshToken();
    public string StaffId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public string? DeviceInfo { get; set; }
    public string? IpAddress { get; set; }
    /// <summary>Set on first issuance; copied on rotation so the 12 h absolute cap chains correctly.</summary>
    public DateTime AbsoluteCreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Random opaque token identifying this session (e.g. for "is_current" comparison).</summary>
    public string? SessionToken { get; set; }

    // Navigation
    public Staff? Staff { get; set; }
}

public class TwoFaChallenge : BaseEntity
{
    public TwoFaChallenge() => Id = EntityId.For2FaChallenge();
    public string StaffId { get; set; } = string.Empty;
    public string ChallengeTokenHash { get; set; } = string.Empty;
    public int AttemptCount { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
}

public class TwoFaRecoveryCode : BaseEntity
{
    public TwoFaRecoveryCode() => Id = EntityId.For2FaRecoveryCode();
    public string StaffId { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public bool IsUsed { get; set; }
}

public class StaffInvite : BaseEntity
{
    public StaffInvite() => Id = EntityId.ForStaffInvite();
    public string TokenHash { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string RoleKey { get; set; } = string.Empty;
    public string MarketsJson { get; set; } = "[]";
    public string InvitedByStaffId { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
}

public class StaffPasswordReset : BaseEntity
{
    public StaffPasswordReset() => Id = EntityId.ForStaffPasswordReset();
    public string TokenHash { get; set; } = string.Empty;
    public string StaffId { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
}

public class DispatchConfig : BaseEntity
{
    public DispatchConfig() => Id = EntityId.ForDispatchConfig();
    public string Market { get; set; } = string.Empty;
    public int OfferTimeoutSeconds { get; set; } = 15;
    public int SearchRadiusM { get; set; } = 3000;
    public string Strategy { get; set; } = "broadcast"; // sequential | broadcast
    public int MaxConcurrentOffers { get; set; } = 3;
    public int LocationPingOnTripS { get; set; } = 5;
    public int LocationPingIdleS { get; set; } = 20;
    public string? UpdatedByStaffId { get; set; }
    public new DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class FeatureFlag : BaseEntity
{
    public FeatureFlag() => Id = EntityId.ForFeatureFlag();
    public string Key { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public int RolloutPct { get; set; } = 100;
    public string Scope { get; set; } = "global";
    public string Market { get; set; } = string.Empty;
    public string? UpdatedByStaffId { get; set; }
    public new DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Integration : BaseEntity
{
    public Integration() => Id = EntityId.ForIntegration();
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool IsConnected { get; set; }
    public string? LastError { get; set; }
    public DateTime? KeyLastRotatedAt { get; set; }
    public string Market { get; set; } = string.Empty;
}

public class AuditLog : BaseEntity
{
    public AuditLog() => Id = EntityId.ForAuditLog();
    public string ActorId { get; set; } = string.Empty;   // staff_id
    public string ActorName { get; set; } = string.Empty;
    public AuditAction Action { get; set; }
    public string? TargetType { get; set; }               // Trip | Rider | Driver | Staff | Wallet
    public string? TargetId { get; set; }
    public string? IpAddress { get; set; }
    public string? RequestId { get; set; }
    public string? Reason { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string Market { get; set; } = string.Empty;
}

public class BackgroundJob : BaseEntity
{
    public BackgroundJob() => Id = EntityId.ForBackgroundJob();
    public string Type { get; set; } = string.Empty;        // export | bulk_approve | broadcast | report
    public string Status { get; set; } = "queued";          // queued | running | completed | failed
    public decimal Progress { get; set; }
    public string? ResultUrl { get; set; }
    public string? Error { get; set; }
    public string? InitiatedByStaffId { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>Persists admin-edited permission sets for a role. Overrides the static AdminRoles defaults.</summary>
public class StaffRoleCustomization : BaseEntity
{
    public StaffRoleCustomization() => Id = EntityId.ForRoleCustomization();
    public string RoleKey { get; set; } = string.Empty;
    public string PermissionsJson { get; set; } = "[]";
    public string? UpdatedByStaffId { get; set; }
    public new DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class OtpRecord : BaseEntity
{
    public OtpRecord() => Id = EntityId.ForOtpRecord();

    /// <summary>Phone number (E.164) for phone OTPs, email address for email OTPs.</summary>
    public string Identifier { get; set; } = string.Empty;
    public string OtpToken { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public OtpPurpose Purpose { get; set; }
    public string? Role { get; set; }
    public int AttemptCount { get; set; }
    public bool IsUsed { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsNewUser { get; set; }
}

public class IdempotencyRecord : BaseEntity
{
    public IdempotencyRecord() => Id = EntityId.ForIdempotency();

    public string Key { get; set; } = string.Empty;
    public string? UserId { get; set; }   // nullable: anonymous calls (e.g. register) use null
    public string Endpoint { get; set; } = string.Empty;
    public string ResponseJson { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class Incentive : BaseEntity
{
    public Incentive() => Id = EntityId.ForIncentive();
    public string Title { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string ConditionJson { get; set; } = "{}"; // trips / hours / zone / time_window
    public long Reward { get; set; }
    public string Currency { get; set; } = "XOF";
    public long? BudgetCap { get; set; }
    public long BudgetUsed { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    public string Market { get; set; } = string.Empty;
}
