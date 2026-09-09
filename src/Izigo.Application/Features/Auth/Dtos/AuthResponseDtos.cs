namespace Izigo.Application.Features.Auth.Dtos;

// Returned by every successful sign-in (OTP verify, register, login, social, biometric, refresh)
public record AuthBundleDto(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string RefreshToken,
    int RefreshExpiresIn,
    AuthUserDto User,
    AuthDriverDto? Driver,      // present when role = driver
    string NextStep             // home | verify_phone | complete_profile | kyc
);

public record AuthUserDto(
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
    DateTime CreatedAt,
    decimal Rating,
    long WalletBalance,
    string Currency
);

public record AuthDriverDto(
    string KycStatus,
    bool OnboardingComplete,
    bool IsOnline,
    string[] VerticalsAllowed,
    AuthVehicleDto? Vehicle
);

public record AuthVehicleDto(string Type, string MakeModel, string Plate);

public record OtpResponseDto(
    string OtpToken,
    int ExpiresIn,
    int ResendAfter,
    bool IsNewUser,
    string? DevCode = null
);

public record CheckAvailabilityDto(bool Available);

public record ForgotPasswordDto(string ResetToken, int ExpiresIn);

public record VerifyResetCodeDto(bool Valid);

public record SessionDto(
    string Id,
    string DeviceName,    // "{Model} ({Platform})" e.g. "Pixel 6 (Android)"
    string Platform,
    string? Ip,
    string? Location,     // coarse location resolved from IP
    DateTime LastActiveAt,
    bool IsCurrent
);

public record BiometricEnrollDto(string BiometricToken, DateTime ExpiresAt);

// Social auth when phone hasn't been bound yet
public record SocialPendingDto(bool NeedsPhone, string SocialBindToken);
