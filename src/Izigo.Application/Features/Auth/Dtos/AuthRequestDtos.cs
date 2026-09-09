namespace Izigo.Application.Features.Auth.Dtos;

public record RequestOtpRequest(
    string Phone,
    string Role,
    string Purpose
);

public record VerifyOtpRequest(
    string OtpToken,
    string Code,
    DeviceDto Device
);

public record ResendOtpRequest(
    string OtpToken,
    string? Channel
);

public record RegisterRequest(
    string FirstName,
    string LastName,
    string Phone,
    string? Email,
    string? Password,
    string Role,
    string? ReferralCode,
    string Language,
    DeviceDto Device
);

public record LoginRequest(
    // 'identifier' is the current field; 'email_or_phone' is the legacy field kept for migration
    string? Identifier,
    string? EmailOrPhone,   // legacy — honoured if Identifier is absent
    string Password,
    string Role,
    DeviceDto Device
)
{
    /// <summary>Returns whichever identifier field was provided, preferring the current one.</summary>
    public string ResolvedIdentifier => Identifier ?? EmailOrPhone
        ?? throw new ArgumentException("VALIDATION_ERROR: identifier is required.");
}

public record SocialLoginRequest(
    string Provider,
    string IdToken,
    string Role,
    DeviceDto Device
);

public record RefreshTokenRequest(string RefreshToken);

public record CheckAvailabilityRequest(
    string? Phone,
    string? Email,
    string Role
);

public record ForgotPasswordRequest(string Identifier, string Role);

public record VerifyResetCodeRequest(string ResetToken, string Code);

public record ResetPasswordRequest(
    string ResetToken,
    string Code,
    string Password,
    string PasswordConfirmation
);

public record ChangePasswordRequest(
    string CurrentPassword,
    string Password,
    string PasswordConfirmation
);

public record EnrollBiometricRequest(
    string PublicKeyOrSecret
);

public record BiometricLoginRequest(
    string BiometricToken,
    string DeviceId
);

public record DeviceDto(
    string DeviceId,
    string Platform,
    string? AppVersion,
    string? OsVersion,
    string? Model,
    string? Locale,
    string? Timezone,
    string? FcmToken
);
