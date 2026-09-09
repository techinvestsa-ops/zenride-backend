using System.Security.Cryptography;
using System.Text;
using Izigo.Application.Common;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Security;
using Izigo.Application.Common.Settings;
using Izigo.Application.Features.Admin.Auth.Dtos;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Izigo.Application.Features.Admin.Auth.Commands;

// ── Helpers ──────────────────────────────────────────────────────────────────────

file static class AdminAuthHelpers
{
    public static string Sha256(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLower();
    }

    public static StaffDto ToDto(Staff staff)
    {
        var role = AdminRoles.Roles.TryGetValue(staff.RoleKey, out var r)
            ? new StaffRoleDto(r.Key, r.Label, r.Rank)
            : new StaffRoleDto(staff.RoleKey, staff.RoleKey, 0);

        var perms = AdminRoles.GetEffectivePermissions(staff);

        return new StaffDto(
            staff.Id, staff.Name, staff.Email, staff.Phone,
            role, perms,
            new StaffPermissionOverridesDto(staff.GrantedPermissions, staff.RevokedPermissions),
            staff.Markets,
            staff.TwoFaEnabled,
            staff.MustChangePassword,
            staff.Status.ToString().ToLower());
    }

    public static SessionBundleDto BuildBundle(Staff staff, StaffRefreshToken rt, string accessToken)
        => new(
            AccessToken: accessToken,
            ExpiresIn: 900,
            RefreshToken: rt.Token,
            RefreshExpiresIn: 43200,
            Staff: ToDto(staff));
}

// ─────────────────────────────────────────────────────────────────────────────────
// LOGIN
// ─────────────────────────────────────────────────────────────────────────────────

public record AdminLoginCommand(string Email, string Password)
    : IRequest<AdminLoginResult>;

/// <summary>Discriminated union result: session bundle OR 2FA challenge.</summary>
public record AdminLoginResult(
    SessionBundleDto? Bundle,
    TwoFaChallengeResponseDto? Challenge,
    string? ErrorCode);

public class AdminLoginHandler(
    IApplicationDbContext db,
    IPasswordHasher hasher,
    ITokenService tokens,
    IOptions<AdminAuthSettings> authOptions) : IRequestHandler<AdminLoginCommand, AdminLoginResult>
{
    public async Task<AdminLoginResult> Handle(AdminLoginCommand cmd, CancellationToken ct)
    {
        var staff = await db.Staff
            .Include(s => s.RefreshTokens)
            .FirstOrDefaultAsync(s => s.Email == cmd.Email, ct);

        if (staff is null || !hasher.Verify(cmd.Password, staff.PasswordHash))
            return new(null, null, "INVALID_CREDENTIALS");

        if (staff.Status == StaffStatus.Blocked)
            return new(null, null, "ACCOUNT_BLOCKED");

        if (staff.Status == StaffStatus.Suspended)
            return new(null, null, "ACCOUNT_SUSPENDED");

        staff.LastActiveAt = DateTime.UtcNow;

        if (staff.TwoFaEnabled)
        {
            // Issue 2FA challenge
            var plainToken = Guid.CreateVersion7().ToString("N");
            var challenge  = new TwoFaChallenge
            {
                StaffId            = staff.Id,
                ChallengeTokenHash = AdminAuthHelpers.Sha256(plainToken),
                ExpiresAt          = DateTime.UtcNow.AddSeconds(authOptions.Value.TwoFaChallengeExpirySeconds)
            };
            db.TwoFaChallenges.Add(challenge);
            await db.SaveChangesAsync(ct);

            return new(null,
                new TwoFaChallengeResponseDto(true, plainToken, authOptions.Value.TwoFaChallengeExpirySeconds),
                null);
        }

        // No 2FA — issue session directly
        var accessToken = tokens.GenerateAdminAccessToken(staff);
        var rawRefresh  = tokens.GenerateAdminRefreshToken();
        var sessionTok  = Guid.CreateVersion7().ToString("N");

        var rt = new StaffRefreshToken
        {
            StaffId             = staff.Id,
            Token               = rawRefresh,
            ExpiresAt           = DateTime.UtcNow.AddHours(authOptions.Value.RefreshExpiryHours),
            AbsoluteCreatedAt   = DateTime.UtcNow,
            SessionToken        = sessionTok,
            IpAddress           = null,   // set at controller level if needed
            DeviceInfo          = null
        };
        db.StaffRefreshTokens.Add(rt);
        await db.SaveChangesAsync(ct);

        return new(AdminAuthHelpers.BuildBundle(staff, rt, accessToken), null, null);
    }
}

// ─────────────────────────────────────────────────────────────────────────────────
// VERIFY 2FA
// ─────────────────────────────────────────────────────────────────────────────────

public record Verify2FaCommand(string ChallengeToken, string Code)
    : IRequest<Verify2FaResult>;

public record Verify2FaResult(SessionBundleDto? Bundle, string? ErrorCode);

public class Verify2FaHandler(
    IApplicationDbContext db,
    ITokenService tokens) : IRequestHandler<Verify2FaCommand, Verify2FaResult>
{
    public async Task<Verify2FaResult> Handle(Verify2FaCommand cmd, CancellationToken ct)
    {
        var hash      = AdminAuthHelpers.Sha256(cmd.ChallengeToken);
        var challenge = await db.TwoFaChallenges
            .FirstOrDefaultAsync(c => c.ChallengeTokenHash == hash && !c.IsUsed, ct);

        if (challenge is null)
            return new(null, "2FA_EXPIRED");

        if (challenge.ExpiresAt < DateTime.UtcNow)
        {
            challenge.IsUsed = true;
            await db.SaveChangesAsync(ct);
            return new(null, "2FA_EXPIRED");
        }

        if (challenge.AttemptCount >= 5)
        {
            return new(null, "2FA_LOCKED");
        }

        var staff = await db.Staff
            .FirstOrDefaultAsync(s => s.Id == challenge.StaffId, ct);

        if (staff is null)
            return new(null, "2FA_EXPIRED");

        // Try TOTP code
        bool codeOk = false;
        if (!string.IsNullOrWhiteSpace(staff.TwoFaSecret))
            codeOk = Totp.Verify(staff.TwoFaSecret, cmd.Code);

        // Try recovery code
        if (!codeOk)
        {
            var codeHash    = Totp.HashCode(cmd.Code);
            var recoveryCode = await db.TwoFaRecoveryCodes
                .FirstOrDefaultAsync(r => r.StaffId == staff.Id && r.CodeHash == codeHash && !r.IsUsed, ct);

            if (recoveryCode is not null)
            {
                recoveryCode.IsUsed = true;
                codeOk = true;
            }
        }

        if (!codeOk)
        {
            challenge.AttemptCount++;
            await db.SaveChangesAsync(ct);
            if (challenge.AttemptCount >= 5)
                return new(null, "2FA_LOCKED");
            return new(null, "INVALID_CODE");
        }

        challenge.IsUsed = true;
        staff.LastActiveAt = DateTime.UtcNow;

        var accessToken = tokens.GenerateAdminAccessToken(staff);
        var rawRefresh  = tokens.GenerateAdminRefreshToken();
        var sessionTok  = Guid.CreateVersion7().ToString("N");

        var rt = new StaffRefreshToken
        {
            StaffId           = staff.Id,
            Token             = rawRefresh,
            ExpiresAt         = DateTime.UtcNow.AddHours(12),
            AbsoluteCreatedAt = DateTime.UtcNow,
            SessionToken      = sessionTok
        };
        db.StaffRefreshTokens.Add(rt);
        await db.SaveChangesAsync(ct);

        return new(AdminAuthHelpers.BuildBundle(staff, rt, accessToken), null);
    }
}

// ─────────────────────────────────────────────────────────────────────────────────
// ENROLL 2FA
// ─────────────────────────────────────────────────────────────────────────────────

public record Enroll2FaCommand(string StaffId) : IRequest<Enroll2FaResult>;
public record Enroll2FaResult(TwoFaEnrollResponseDto? Dto, string? ErrorCode);

public class Enroll2FaHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<Enroll2FaCommand, Enroll2FaResult>
{
    public async Task<Enroll2FaResult> Handle(Enroll2FaCommand cmd, CancellationToken ct)
    {
        var staff = await db.Staff.FirstOrDefaultAsync(s => s.Id == cmd.StaffId, ct);
        if (staff is null) return new(null, "NOT_FOUND");
        if (staff.TwoFaEnabled) return new(null, "ALREADY_ENROLLED");

        var secret = Totp.GenerateSecret();
        var uri    = Totp.GetUri(secret, staff.Email, "Izigo Admin");
        var (plain, hashed) = Totp.GenerateRecoveryCodes();

        staff.TwoFaSecret  = secret;
        staff.TwoFaEnabled = true;

        // Remove any old recovery codes
        var oldCodes = db.TwoFaRecoveryCodes.Where(r => r.StaffId == staff.Id);
        db.TwoFaRecoveryCodes.RemoveRange(oldCodes);

        foreach (var h in hashed)
            db.TwoFaRecoveryCodes.Add(new TwoFaRecoveryCode { StaffId = staff.Id, CodeHash = h });

        await audit.RecordAsync(staff.Id, staff.Name, AuditAction.TwoFaEnroll, "Staff", staff.Id, ct: ct);
        await db.SaveChangesAsync(ct);

        return new(new TwoFaEnrollResponseDto(uri, plain), null);
    }
}

// ─────────────────────────────────────────────────────────────────────────────────
// REFRESH TOKEN
// ─────────────────────────────────────────────────────────────────────────────────

public record AdminRefreshCommand(string RefreshToken) : IRequest<AdminRefreshResult>;
public record AdminRefreshResult(SessionBundleDto? Bundle, string? ErrorCode);

public class AdminRefreshHandler(IApplicationDbContext db, ITokenService tokens)
    : IRequestHandler<AdminRefreshCommand, AdminRefreshResult>
{
    public async Task<AdminRefreshResult> Handle(AdminRefreshCommand cmd, CancellationToken ct)
    {
        var rt = await db.StaffRefreshTokens
            .Include(r => r.Staff)
            .FirstOrDefaultAsync(r => r.Token == cmd.RefreshToken && !r.IsRevoked, ct);

        if (rt is null || rt.ExpiresAt < DateTime.UtcNow)
            return new(null, "INVALID_REFRESH_TOKEN");

        // Absolute 12 h cap check
        if (rt.AbsoluteCreatedAt.AddHours(12) < DateTime.UtcNow)
            return new(null, "SESSION_EXPIRED");

        var staff = rt.Staff;
        if (staff is null)
            return new(null, "INVALID_REFRESH_TOKEN");

        // Rotate
        rt.IsRevoked = true;

        var newRawRefresh = tokens.GenerateAdminRefreshToken();
        var sessionTok    = rt.SessionToken ?? Guid.CreateVersion7().ToString("N");
        var newRt = new StaffRefreshToken
        {
            StaffId           = staff.Id,
            Token             = newRawRefresh,
            ExpiresAt         = DateTime.UtcNow.AddHours(12),
            AbsoluteCreatedAt = rt.AbsoluteCreatedAt,   // chain the cap
            SessionToken      = sessionTok,
            IpAddress         = rt.IpAddress,
            DeviceInfo        = rt.DeviceInfo
        };
        db.StaffRefreshTokens.Add(newRt);

        var accessToken = tokens.GenerateAdminAccessToken(staff);
        await db.SaveChangesAsync(ct);

        return new(AdminAuthHelpers.BuildBundle(staff, newRt, accessToken), null);
    }
}

// ─────────────────────────────────────────────────────────────────────────────────
// LOGOUT
// ─────────────────────────────────────────────────────────────────────────────────

public record AdminLogoutCommand(string StaffId, string? CurrentSessionToken) : IRequest;

public class AdminLogoutHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<AdminLogoutCommand>
{
    public async Task Handle(AdminLogoutCommand cmd, CancellationToken ct)
    {
        var rt = await db.StaffRefreshTokens
            .FirstOrDefaultAsync(r =>
                r.StaffId == cmd.StaffId &&
                r.SessionToken == cmd.CurrentSessionToken &&
                !r.IsRevoked, ct);

        if (rt is not null)
            rt.IsRevoked = true;

        var staff = await db.Staff.FindAsync([cmd.StaffId], ct);
        if (staff is not null)
            await audit.RecordAsync(staff.Id, staff.Name, AuditAction.Logout, ct: ct);

        await db.SaveChangesAsync(ct);
    }
}

// ─────────────────────────────────────────────────────────────────────────────────
// CHANGE PASSWORD
// ─────────────────────────────────────────────────────────────────────────────────

public record ChangeMyPasswordCommand(
    string StaffId,
    string CurrentPassword,
    string NewPassword,
    string PasswordConfirmation,
    string? CurrentSessionToken) : IRequest<ChangePasswordResult>;

public record ChangePasswordResult(bool Success, string? ErrorCode);

public class ChangeMyPasswordHandler(
    IApplicationDbContext db,
    IPasswordHasher hasher,
    IAuditService audit) : IRequestHandler<ChangeMyPasswordCommand, ChangePasswordResult>
{
    public async Task<ChangePasswordResult> Handle(ChangeMyPasswordCommand cmd, CancellationToken ct)
    {
        if (cmd.NewPassword != cmd.PasswordConfirmation)
            return new(false, "PASSWORD_MISMATCH");

        if (cmd.NewPassword.Length < 12)
            return new(false, "PASSWORD_TOO_SHORT");

        var staff = await db.Staff.FindAsync([cmd.StaffId], ct);
        if (staff is null) return new(false, "NOT_FOUND");

        if (!hasher.Verify(cmd.CurrentPassword, staff.PasswordHash))
            return new(false, "INVALID_CURRENT_PASSWORD");

        staff.PasswordHash      = hasher.Hash(cmd.NewPassword);
        staff.MustChangePassword = false;

        // Revoke all OTHER sessions
        var others = await db.StaffRefreshTokens
            .Where(r => r.StaffId == cmd.StaffId &&
                        r.SessionToken != cmd.CurrentSessionToken &&
                        !r.IsRevoked)
            .ToListAsync(ct);

        foreach (var r in others) r.IsRevoked = true;

        await audit.RecordAsync(staff.Id, staff.Name, AuditAction.PasswordChange, "Staff", staff.Id, ct: ct);
        await db.SaveChangesAsync(ct);

        return new(true, null);
    }
}

// ─────────────────────────────────────────────────────────────────────────────────
// REVOKE OTHER SESSIONS
// ─────────────────────────────────────────────────────────────────────────────────

public record RevokeOtherSessionsCommand(string StaffId, string? CurrentSessionToken) : IRequest;

public class RevokeOtherSessionsHandler(IApplicationDbContext db)
    : IRequestHandler<RevokeOtherSessionsCommand>
{
    public async Task Handle(RevokeOtherSessionsCommand cmd, CancellationToken ct)
    {
        var others = await db.StaffRefreshTokens
            .Where(r => r.StaffId == cmd.StaffId &&
                        r.SessionToken != cmd.CurrentSessionToken &&
                        !r.IsRevoked)
            .ToListAsync(ct);

        foreach (var r in others) r.IsRevoked = true;
        await db.SaveChangesAsync(ct);
    }
}

// ─────────────────────────────────────────────────────────────────────────────────
// FORGOT PASSWORD
// ─────────────────────────────────────────────────────────────────────────────────

public record ForgotPasswordCommand(string Email) : IRequest;

public class ForgotPasswordHandler(
    IApplicationDbContext db,
    IEmailService email,
    IOptions<AdminAuthSettings> authOptions)
    : IRequestHandler<ForgotPasswordCommand>
{
    public async Task Handle(ForgotPasswordCommand cmd, CancellationToken ct)
    {
        // Always 200 — never reveal whether email exists
        var staff = await db.Staff
            .FirstOrDefaultAsync(s => s.Email == cmd.Email, ct);

        if (staff is not null)
        {
            var plainToken = Guid.CreateVersion7().ToString("N");
            var reset = new StaffPasswordReset
            {
                StaffId   = staff.Id,
                TokenHash = AdminAuthHelpers.Sha256(plainToken),
                ExpiresAt = DateTime.UtcNow.AddMinutes(authOptions.Value.ForgotPasswordExpiryMinutes)
            };
            db.StaffPasswordResets.Add(reset);
            await db.SaveChangesAsync(ct);

            var resetUrl = email.BuildLink($"/admin/auth/reset-password?token={plainToken}");
            await email.SendAsync(staff.Email, staff.Name,
                "Reset your Zenride Admin password",
                EmailTemplates.PasswordResetRequested(staff.Name, resetUrl), ct);
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────────
// RESET PASSWORD
// ─────────────────────────────────────────────────────────────────────────────────

public record ResetPasswordCommand(string Token, string Password) : IRequest<ResetPasswordResult>;
public record ResetPasswordResult(bool Success, string? ErrorCode);

public class ResetPasswordHandler(
    IApplicationDbContext db,
    IPasswordHasher hasher,
    IAuditService audit) : IRequestHandler<ResetPasswordCommand, ResetPasswordResult>
{
    public async Task<ResetPasswordResult> Handle(ResetPasswordCommand cmd, CancellationToken ct)
    {
        if (cmd.Password.Length < 12)
            return new(false, "PASSWORD_TOO_SHORT");

        var hash  = AdminAuthHelpers.Sha256(cmd.Token);
        var reset = await db.StaffPasswordResets
            .FirstOrDefaultAsync(r => r.TokenHash == hash && !r.IsUsed, ct);

        if (reset is null || reset.ExpiresAt < DateTime.UtcNow)
            return new(false, "INVALID_TOKEN");

        var staff = await db.Staff.FindAsync([reset.StaffId], ct);
        if (staff is null)
            return new(false, "INVALID_TOKEN");

        reset.IsUsed             = true;
        staff.PasswordHash       = hasher.Hash(cmd.Password);
        staff.MustChangePassword = false;

        // Revoke all sessions
        var sessions = await db.StaffRefreshTokens
            .Where(r => r.StaffId == staff.Id && !r.IsRevoked)
            .ToListAsync(ct);
        foreach (var s in sessions) s.IsRevoked = true;

        await audit.RecordAsync(staff.Id, staff.Name, AuditAction.PasswordReset, "Staff", staff.Id, ct: ct);
        await db.SaveChangesAsync(ct);

        return new(true, null);
    }
}

// ─────────────────────────────────────────────────────────────────────────────────
// ACCEPT INVITE
// ─────────────────────────────────────────────────────────────────────────────────

public record AcceptInviteCommand(string Token, string Password) : IRequest<AcceptInviteResult>;
public record AcceptInviteResult(bool Success, string? ErrorCode, bool RequiresTwoFaEnrollment = false);

public class AcceptInviteHandler(
    IApplicationDbContext db,
    IPasswordHasher hasher,
    IAuditService audit) : IRequestHandler<AcceptInviteCommand, AcceptInviteResult>
{
    public async Task<AcceptInviteResult> Handle(AcceptInviteCommand cmd, CancellationToken ct)
    {
        if (cmd.Password.Length < 12)
            return new(false, "PASSWORD_TOO_SHORT");

        var hash   = AdminAuthHelpers.Sha256(cmd.Token);
        var invite = await db.StaffInvites
            .FirstOrDefaultAsync(i => i.TokenHash == hash && !i.IsUsed, ct);

        if (invite is null || invite.ExpiresAt < DateTime.UtcNow)
            return new(false, "INVALID_TOKEN");

        // Find or create the staff record (invite may pre-create it)
        var staff = await db.Staff
            .FirstOrDefaultAsync(s => s.Email == invite.Email, ct);

        if (staff is null)
        {
            var markets = System.Text.Json.JsonSerializer
                .Deserialize<List<string>>(invite.MarketsJson) ?? [];

            staff = new Staff
            {
                Email   = invite.Email,
                Name    = invite.Email, // will be updated on first profile edit
                RoleKey = invite.RoleKey,
                Markets = markets,
                MustChangePassword = false
            };
            db.Staff.Add(staff);
        }

        staff.PasswordHash       = hasher.Hash(cmd.Password);
        staff.MustChangePassword = false;
        invite.IsUsed            = true;

        await audit.RecordAsync(staff.Id, staff.Name, AuditAction.InviteAccept, "Staff", staff.Id, ct: ct);
        await db.SaveChangesAsync(ct);

        // Check if role requires 2FA (money permissions)
        var perms     = AdminRoles.GetEffectivePermissions(staff);
        var needsTwoFa = perms.Any(p => AdminRoles.MoneyPermissions.Contains(p));

        return new(true, null, needsTwoFa && !staff.TwoFaEnabled);
    }
}
