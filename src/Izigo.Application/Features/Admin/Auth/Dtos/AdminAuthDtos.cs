namespace Izigo.Application.Features.Admin.Auth.Dtos;

// ── Response shapes ─────────────────────────────────────────────────────────────

public record StaffRoleDto(string Key, string Label, int Rank);

public record StaffPermissionOverridesDto(List<string> Granted, List<string> Revoked);

public record StaffDto(
    string Id,
    string Name,
    string Email,
    string? Phone,
    StaffRoleDto Role,
    string[] Permissions,
    StaffPermissionOverridesDto PermissionOverrides,
    List<string> Markets,
    bool TwofaEnabled,
    bool MustChangePassword,
    string Status);

public record SessionBundleDto(
    string AccessToken,
    int ExpiresIn,
    string RefreshToken,
    int RefreshExpiresIn,
    StaffDto Staff);

public record TwoFaChallengeResponseDto(
    bool Requires2fa,
    string ChallengeToken,
    int ExpiresIn);

public record TwoFaEnrollResponseDto(
    string ProvisioningUri,
    string[] RecoveryCodes);

public record StaffSessionDto(
    string SessionToken,
    string? DeviceInfo,
    string? IpAddress,
    string? CoarseLocation,
    DateTime LastSeen,
    bool IsCurrent);

// ── Request shapes ──────────────────────────────────────────────────────────────

public record AdminLoginRequest(string Email, string Password);

public record TwoFaVerifyRequest(string ChallengeToken, string Code);

public record AdminRefreshRequest(string RefreshToken);

public record ChangePasswordRequest(
    string CurrentPassword,
    string Password,
    string PasswordConfirmation);

public record ForgotPasswordRequest(string Email);

public record ResetPasswordRequest(string Token, string Password);

public record AcceptInviteRequest(string Token, string Password);
