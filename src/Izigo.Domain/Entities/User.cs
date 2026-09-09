using Izigo.Domain.Common;
using Izigo.Domain.Enums;

namespace Izigo.Domain.Entities;

public class User : AuditableEntity
{
    public User() => Id = EntityId.ForUser();

    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public bool PhoneVerified { get; set; }
    public string? Email { get; set; }
    public bool EmailVerified { get; set; }
    public string? PasswordHash { get; set; }
    public UserRole Role { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Active;
    public string? PhotoUrl { get; set; }
    public string Language { get; set; } = "fr";
    public string? SuspensionReason { get; set; }
    public DateTime? SuspendedUntil { get; set; }
    public string? ReferralCode { get; set; }
    public string? ReferredByUserId { get; set; }
    public bool MarketingOptIn { get; set; }
    public decimal Rating { get; set; } = 5.0m;
    public int TotalRatings { get; set; }
    public string? Gender { get; set; }
    public DateOnly? DateOfBirth { get; set; }
    public bool DeletionRequested { get; set; }
    public DateTime? DeletionRequestedAt { get; set; }
    public string? DeletionReason { get; set; }

    // Flagging — does not block the account, surfaces on every admin screen
    public bool IsFlagged { get; set; }
    public string? FlagReason { get; set; }
    public string? FlagSeverity { get; set; }   // low | medium | high

    public DriverProfile? DriverProfile { get; set; }
    public Wallet? Wallet { get; set; }
    public ICollection<UserDevice> Devices { get; set; } = [];
    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<SavedPlace> SavedPlaces { get; set; } = [];
    public ICollection<EmergencyContact> EmergencyContacts { get; set; } = [];
    public ICollection<UserPreference> Preferences { get; set; } = [];
    public ICollection<BiometricToken> BiometricTokens { get; set; } = [];

    public string FullName => $"{FirstName} {LastName}".Trim();
}

public class DriverProfile : BaseEntity
{
    public DriverProfile() => Id = EntityId.ForDriver();

    public string UserId { get; set; } = string.Empty;
    public User User { get; set; } = null!;
    public KycStatus KycStatus { get; set; } = KycStatus.Pending;
    public bool OnboardingComplete { get; set; }
    public bool IsOnline { get; set; }
    public List<Vertical> VerticalsAllowed { get; set; } = [];
    public decimal? LastLat { get; set; }
    public decimal? LastLng { get; set; }
    public decimal? LastHeading { get; set; }
    public DateTime? LastLocationAt { get; set; }
    public int OnlineSecondsToday { get; set; }
    public decimal AcceptanceRate { get; set; } = 1.0m;
    public decimal CancellationRate { get; set; }
    public decimal CompletionRate { get; set; } = 1.0m;
    public int TotalTripsCompleted { get; set; }
    public string? BlockReason { get; set; }

    public ICollection<Vehicle> Vehicles { get; set; } = [];
    public ICollection<DriverDocument> Documents { get; set; } = [];
    public DriverOnboarding? Onboarding { get; set; }
    public DriverWallet? DriverWallet { get; set; }
    public ICollection<PayoutMethod> PayoutMethods { get; set; } = [];
}

public class UserDevice : BaseEntity
{
    public UserDevice() => Id = EntityId.ForDevice();

    public string UserId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string? FcmToken { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string? AppVersion { get; set; }
    public string? OsVersion { get; set; }
    public string? Model { get; set; }
    public string? Locale { get; set; }
    public string? Timezone { get; set; }
    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;
    // Stored at login time for the sessions endpoint
    public string? LastIpAddress { get; set; }
    public string? LastLocation { get; set; }   // coarse location resolved from IP
}

public class RefreshToken : BaseEntity
{
    public RefreshToken() => Id = EntityId.ForRefreshToken();

    public string UserId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public string? DeviceId { get; set; }
}

public class SavedPlace : BaseEntity
{
    public SavedPlace() => Id = EntityId.ForSavedPlace();

    public string UserId { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public decimal Lat { get; set; }
    public decimal Lng { get; set; }
    public string? PlaceId { get; set; }
    public string? Building { get; set; }
    public string? Floor { get; set; }
    public string? Instructions { get; set; }
    public string? ContactPhone { get; set; }
    public bool IsDefault { get; set; }
}

public class EmergencyContact : BaseEntity
{
    public EmergencyContact() => Id = EntityId.ForEmergencyContact();

    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Relationship { get; set; }
    public bool NotifyOnTripStart { get; set; }
}

public class UserPreference : BaseEntity
{
    public UserPreference() => Id = EntityId.ForUserPreference();
    public string UserId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

public class BiometricToken : BaseEntity
{
    public BiometricToken() => Id = EntityId.ForBiometricToken();

    public string UserId { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    // Stored as SHA-256 hash — raw token returned to client only at enrolment
    public string TokenHash { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime ExpiresAt { get; set; }
}
