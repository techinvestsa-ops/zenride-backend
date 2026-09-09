namespace Izigo.Application.Features.Profile.Dtos;

// ── GET /me ─────────────────────────────────────────────────────────────────

public record UserProfileDto(
    string Id,
    string Role,
    string FirstName,
    string LastName,
    string Phone,
    bool PhoneVerified,
    string? Email,
    bool EmailVerified,
    string? PhotoUrl,
    string Language,
    string? Gender,
    string? DateOfBirth,   // ISO-8601 date
    DateTime CreatedAt,
    decimal Rating,
    long WalletBalance,
    string Currency
);

// ── PATCH /me ────────────────────────────────────────────────────────────────

public record UpdateProfileRequest(
    string? FirstName,
    string? LastName,
    string? Email,
    string? Language,
    string? Gender,
    string? DateOfBirth
);

// ── POST /me/photo ───────────────────────────────────────────────────────────

public record PhotoUploadResult(string PhotoUrl);

// ── GET /me/stats ────────────────────────────────────────────────────────────

public record RiderStatsDto(
    int TotalTrips,
    long WalletBalance,
    decimal Rating,
    DateTime MemberSince,
    decimal? Co2SavedKg
);

// ── Phone change ─────────────────────────────────────────────────────────────

public record ChangePhoneRequest(string NewPhone);
public record ChangePhoneResult(string OtpToken, int ExpiresIn);
public record VerifyPhoneRequest(string OtpToken, string Code);

// ── Email verification ────────────────────────────────────────────────────────

public record VerifyEmailRequest(string Code);

// ── Preferences ───────────────────────────────────────────────────────────────

public record PreferencesDto(
    bool IncomingTripRequests,
    bool EarningsPayoutAlerts,
    bool PromotionalPeakHours,
    bool TripUpdates,
    bool ChatMessages,
    string Language,
    string Theme,
    bool MarketingOptIn
);

public record UpdatePreferencesRequest(
    bool? IncomingTripRequests,
    bool? EarningsPayoutAlerts,
    bool? PromotionalPeakHours,
    bool? TripUpdates,
    bool? ChatMessages,
    string? Language,
    string? Theme,
    bool? MarketingOptIn
);

// ── Emergency contacts ────────────────────────────────────────────────────────

public record EmergencyContactDto(
    string Id,
    string Name,
    string Phone,
    string? Relationship,
    bool NotifyOnTripStart
);

public record AddEmergencyContactRequest(
    string Name,
    string Phone,
    string? Relationship,
    bool NotifyOnTripStart
);

// ── Referral ─────────────────────────────────────────────────────────────────

public record ReferralDto(
    string Code,
    string ShareMessage,
    int InvitedCount,
    long EarnedTotal,
    long RewardPerInvite,
    string TermsUrl
);

// ── Close account ─────────────────────────────────────────────────────────────

public record CloseAccountRequest(string Reason, string? PasswordOrOtp);

// ── Export ────────────────────────────────────────────────────────────────────

public record ExportDataResult(string Message);
