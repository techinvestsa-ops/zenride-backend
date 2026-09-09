using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Izigo.Application.Common;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Settings;
using Izigo.Application.Features.Admin.Auth;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Izigo.Application.Features.Admin.StaffManagement.Commands;

public record StaffCommandResult(bool Success, string? ErrorCode, object? Data = null);

// ── Shared rank/self checks ───────────────────────────────────────────────────

file static class StaffGuard
{
    public static int RankOf(string roleKey)
        => AdminRoles.Roles.TryGetValue(roleKey, out var r) ? r.Rank : 0;

    /// <summary>Returns an error code if the actor cannot act on the target, null if allowed.</summary>
    public static string? Check(string actorId, string actorRoleKey,
        string targetId, string targetRoleKey)
    {
        if (actorId == targetId) return "CANNOT_TARGET_SELF";
        var actorRank  = RankOf(actorRoleKey);
        var targetRank = RankOf(targetRoleKey);
        if (actorRank <= targetRank && actorRoleKey != "super_admin") return "INSUFFICIENT_RANK";
        return null;
    }

    public static async Task<string?> LastSuperAdminCheckAsync(
        IApplicationDbContext db, string targetId, CancellationToken ct)
    {
        var superCount = await db.Staff
            .CountAsync(s => s.RoleKey == "super_admin" &&
                             s.Status == StaffStatus.Active &&
                             s.Id != targetId, ct);
        return superCount == 0 ? "LAST_SUPER_ADMIN" : null;
    }
}

// ── POST /admin/staff/invite ──────────────────────────────────────────────────
// Never accept a password at invite time. Single-use 48h link set by invitee.

public record InviteStaffCommand(string Name, string Email, string? Phone,
    string RoleKey, string[] Markets, string ActorId, string ActorRoleKey,
    string ActorName) : IRequest<StaffCommandResult>;

public class InviteStaffHandler(IApplicationDbContext db, IAuditService audit,
    IPasswordHasher hasher, IEmailService email,
    IOptions<AdminAuthSettings> authOptions) : IRequestHandler<InviteStaffCommand, StaffCommandResult>
{
    public async Task<StaffCommandResult> Handle(InviteStaffCommand cmd, CancellationToken ct)
    {
        if (!AdminRoles.Roles.TryGetValue(cmd.RoleKey, out var targetRole))
            return new(false, "INVALID_ROLE");

        var actorRank = StaffGuard.RankOf(cmd.ActorRoleKey);
        if (actorRank <= targetRole.Rank && cmd.ActorRoleKey != "super_admin")
            return new(false, "INSUFFICIENT_RANK");

        var exists = await db.Staff.AnyAsync(s => s.Email == cmd.Email, ct);
        if (exists) return new(false, "EMAIL_ALREADY_EXISTS");

        var tokenRaw  = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenRaw))).ToLower();

        var staff = new Domain.Entities.Staff
        {
            Name    = cmd.Name,
            Email   = cmd.Email,
            Phone   = cmd.Phone,
            RoleKey = cmd.RoleKey,
            Markets = cmd.Markets.ToList(),
            Status  = StaffStatus.Active,
            MustChangePassword = true,
            PasswordHash = hasher.Hash(Guid.NewGuid().ToString()) // placeholder until invite accepted
        };
        db.Staff.Add(staff);

        var invite = new StaffInvite
        {
            TokenHash        = tokenHash,
            Email            = cmd.Email,
            RoleKey          = cmd.RoleKey,
            MarketsJson      = JsonSerializer.Serialize(cmd.Markets),
            InvitedByStaffId = cmd.ActorId,
            ExpiresAt        = DateTime.UtcNow.AddHours(authOptions.Value.InviteExpiryHours)
        };
        db.StaffInvites.Add(invite);

        await audit.RecordAsync(cmd.ActorId, cmd.ActorName, AuditAction.StaffInvite,
            "Staff", staff.Id,
            after: new { Email = cmd.Email, Role = cmd.RoleKey }, ct: ct);
        await db.SaveChangesAsync(ct);

        var inviteUrl = email.BuildLink($"/admin/auth/accept-invite?token={tokenRaw}");
        await email.SendAsync(cmd.Email, cmd.Name,
            "You've been invited to Zenride Admin",
            EmailTemplates.StaffInvite(cmd.Name, cmd.RoleKey, inviteUrl), ct);

        return new(true, null, new
        {
            staff_id   = staff.Id,
            invite_id  = invite.Id,
            invite_url = inviteUrl
        });
    }
}

// ── PATCH /admin/staff/{id} ───────────────────────────────────────────────────
// name, email, phone, markets. Email change requires re-verification.

public record UpdateStaffDetailsCommand(string TargetId, string ActorId,
    string ActorRoleKey, string ActorName,
    string? Name, string? Email, string? Phone, string[]? Markets)
    : IRequest<StaffCommandResult>;

public class UpdateStaffDetailsHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UpdateStaffDetailsCommand, StaffCommandResult>
{
    public async Task<StaffCommandResult> Handle(UpdateStaffDetailsCommand cmd, CancellationToken ct)
    {
        var target = await db.Staff.FirstOrDefaultAsync(s => s.Id == cmd.TargetId, ct);
        if (target is null) return new(false, "STAFF_NOT_FOUND");

        var rankError = StaffGuard.Check(cmd.ActorId, cmd.ActorRoleKey, target.Id, target.RoleKey);
        if (rankError is not null) return new(false, rankError);

        var before = new { target.Name, target.Email, target.Phone, target.Markets };

        if (cmd.Name is not null)    target.Name = cmd.Name;
        if (cmd.Phone is not null)   target.Phone = cmd.Phone;
        if (cmd.Markets is not null) target.Markets = cmd.Markets.ToList();
        if (cmd.Email is not null && cmd.Email != target.Email)
        {
            // Email change: notify both addresses (out of scope here) and flag for re-verification
            target.Email = cmd.Email;
        }

        await audit.RecordAsync(cmd.ActorId, cmd.ActorName, AuditAction.StaffInvite,
            "Staff", cmd.TargetId, before: before, after: new { target.Name, target.Email }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── PUT /admin/staff/{id}/role ────────────────────────────────────────────────
// Clears per-person overrides by default; keep_overrides=true to opt out.

public record ChangeStaffRoleCommand(string TargetId, string ActorId,
    string ActorRoleKey, string ActorName,
    string NewRoleKey, string Reason, bool KeepOverrides)
    : IRequest<StaffCommandResult>;

public class ChangeStaffRoleHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<ChangeStaffRoleCommand, StaffCommandResult>
{
    public async Task<StaffCommandResult> Handle(ChangeStaffRoleCommand cmd, CancellationToken ct)
    {
        if (!AdminRoles.Roles.TryGetValue(cmd.NewRoleKey, out var newRole))
            return new(false, "INVALID_ROLE");

        var actorRank = StaffGuard.RankOf(cmd.ActorRoleKey);
        if (actorRank <= newRole.Rank && cmd.ActorRoleKey != "super_admin")
            return new(false, "INSUFFICIENT_RANK");

        var target = await db.Staff.FirstOrDefaultAsync(s => s.Id == cmd.TargetId, ct);
        if (target is null) return new(false, "STAFF_NOT_FOUND");

        var rankError = StaffGuard.Check(cmd.ActorId, cmd.ActorRoleKey, target.Id, target.RoleKey);
        if (rankError is not null) return new(false, rankError);

        // Last super admin guard — cannot demote last super_admin
        if (target.RoleKey == "super_admin")
        {
            var lastSuperError = await StaffGuard.LastSuperAdminCheckAsync(db, target.Id, ct);
            if (lastSuperError is not null) return new(false, lastSuperError);
        }

        var before = new { target.RoleKey, target.GrantedPermissions, target.RevokedPermissions };
        target.RoleKey = cmd.NewRoleKey;

        bool overridesCleared = false;
        if (!cmd.KeepOverrides)
        {
            target.GrantedPermissions.Clear();
            target.RevokedPermissions.Clear();
            overridesCleared = true;
        }

        await audit.RecordAsync(cmd.ActorId, cmd.ActorName, AuditAction.StaffRoleChange,
            "Staff", cmd.TargetId, reason: cmd.Reason,
            before: before, after: new { target.RoleKey, OverridesCleared = overridesCleared }, ct: ct);
        await db.SaveChangesAsync(ct);

        return new(true, null, new { new_role = cmd.NewRoleKey, overrides_cleared = overridesCleared });
    }
}

// ── PUT /admin/staff/{id}/permissions ─────────────────────────────────────────
// Layer on top of role. Actor cannot grant a permission they do not hold.

public record SetStaffPermissionsCommand(string TargetId, string ActorId,
    string ActorRoleKey, string ActorName,
    string[] Granted, string[] Revoked, string Reason)
    : IRequest<StaffCommandResult>;

public class SetStaffPermissionsHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<SetStaffPermissionsCommand, StaffCommandResult>
{
    public async Task<StaffCommandResult> Handle(SetStaffPermissionsCommand cmd, CancellationToken ct)
    {
        var actor = await db.Staff.FirstOrDefaultAsync(s => s.Id == cmd.ActorId, ct);
        if (actor is null) return new(false, "ACTOR_NOT_FOUND");

        var actorEffective = AdminRoles.GetEffectivePermissions(actor).ToHashSet();

        // Cannot grant what you don't hold
        var cannotGrant = cmd.Granted.FirstOrDefault(p => !actorEffective.Contains(p));
        if (cannotGrant is not null)
            return new(false, "CANNOT_GRANT_WHAT_YOU_LACK");

        var target = await db.Staff.FirstOrDefaultAsync(s => s.Id == cmd.TargetId, ct);
        if (target is null) return new(false, "STAFF_NOT_FOUND");

        var rankError = StaffGuard.Check(cmd.ActorId, cmd.ActorRoleKey, target.Id, target.RoleKey);
        if (rankError is not null) return new(false, rankError);

        var before = new
        {
            granted = target.GrantedPermissions.ToList(),
            revoked = target.RevokedPermissions.ToList()
        };

        foreach (var p in cmd.Granted)
        {
            target.RevokedPermissions.Remove(p);
            if (!target.GrantedPermissions.Contains(p))
                target.GrantedPermissions.Add(p);
        }
        foreach (var p in cmd.Revoked)
        {
            target.GrantedPermissions.Remove(p);
            if (!target.RevokedPermissions.Contains(p))
                target.RevokedPermissions.Add(p);
        }

        var effective = AdminRoles.GetEffectivePermissions(target);

        await audit.RecordAsync(cmd.ActorId, cmd.ActorName, AuditAction.StaffPermissionChange,
            "Staff", cmd.TargetId, reason: cmd.Reason,
            before: before, after: new { granted = target.GrantedPermissions, revoked = target.RevokedPermissions },
            ct: ct);
        await db.SaveChangesAsync(ct);

        AdminRoles.Roles.TryGetValue(target.RoleKey, out var role);
        return new(true, null, new
        {
            staff_id   = target.Id,
            role       = new { key = target.RoleKey, rank = role?.Rank ?? 0 },
            permissions = effective,
            permission_overrides = new
            {
                granted = target.GrantedPermissions,
                revoked = target.RevokedPermissions
            }
        });
    }
}

// ── POST /admin/staff/{id}/password/reset-link ────────────────────────────────
// Sets must_change_password, revokes sessions, emails a single-use link.

public record SendPasswordResetLinkCommand(string TargetId, string ActorId,
    string ActorRoleKey, string ActorName) : IRequest<StaffCommandResult>;

public class SendPasswordResetLinkHandler(IApplicationDbContext db, IAuditService audit,
    IEmailService email,
    IOptions<AdminAuthSettings> authOptions) : IRequestHandler<SendPasswordResetLinkCommand, StaffCommandResult>
{
    public async Task<StaffCommandResult> Handle(SendPasswordResetLinkCommand cmd, CancellationToken ct)
    {
        var target = await db.Staff.Include(s => s.RefreshTokens)
            .FirstOrDefaultAsync(s => s.Id == cmd.TargetId, ct);
        if (target is null) return new(false, "STAFF_NOT_FOUND");

        var rankError = StaffGuard.Check(cmd.ActorId, cmd.ActorRoleKey, target.Id, target.RoleKey);
        if (rankError is not null) return new(false, rankError);

        target.MustChangePassword = true;
        foreach (var rt in target.RefreshTokens) rt.IsRevoked = true;

        var tokenRaw  = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(tokenRaw))).ToLower();

        db.StaffPasswordResets.Add(new StaffPasswordReset
        {
            TokenHash = tokenHash,
            StaffId   = target.Id,
            ExpiresAt = DateTime.UtcNow.AddHours(authOptions.Value.StaffPasswordResetExpiryHours)
        });

        await audit.RecordAsync(cmd.ActorId, cmd.ActorName, AuditAction.PasswordResetLinkSent,
            "Staff", cmd.TargetId, ct: ct);
        await db.SaveChangesAsync(ct);

        var resetUrl = email.BuildLink($"/admin/auth/reset-password?token={tokenRaw}");
        await email.SendAsync(target.Email, target.Name,
            "Your Zenride Admin password has been reset",
            EmailTemplates.PasswordResetByAdmin(target.Name, resetUrl), ct);

        return new(true, null, new { reset_url = resetUrl });
    }
}

// ── POST /admin/staff/{id}/password/set ──────────────────────────────────────
// For locked-out colleague. Forces must_change_password and revokes every session.

public record SetStaffPasswordCommand(string TargetId, string ActorId,
    string ActorRoleKey, string ActorName, string Password, string Reason)
    : IRequest<StaffCommandResult>;

public class SetStaffPasswordHandler(IApplicationDbContext db, IAuditService audit,
    IPasswordHasher hasher) : IRequestHandler<SetStaffPasswordCommand, StaffCommandResult>
{
    public async Task<StaffCommandResult> Handle(SetStaffPasswordCommand cmd, CancellationToken ct)
    {
        if (cmd.Password.Length < 12) return new(false, "PASSWORD_TOO_SHORT");
        if (string.IsNullOrWhiteSpace(cmd.Reason)) return new(false, "REASON_REQUIRED");

        var target = await db.Staff.Include(s => s.RefreshTokens)
            .FirstOrDefaultAsync(s => s.Id == cmd.TargetId, ct);
        if (target is null) return new(false, "STAFF_NOT_FOUND");

        var rankError = StaffGuard.Check(cmd.ActorId, cmd.ActorRoleKey, target.Id, target.RoleKey);
        if (rankError is not null) return new(false, rankError);

        target.PasswordHash        = hasher.Hash(cmd.Password);
        target.MustChangePassword  = true;
        foreach (var rt in target.RefreshTokens) rt.IsRevoked = true;

        await audit.RecordAsync(cmd.ActorId, cmd.ActorName, AuditAction.StaffPasswordReset,
            "Staff", cmd.TargetId, reason: cmd.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/staff/{id}/2fa/reset ─────────────────────────────────────────
// Clears 2FA secret; forces re-enrolment at next sign-in.

public record Reset2FaCommand(string TargetId, string ActorId,
    string ActorRoleKey, string ActorName, string Reason) : IRequest<StaffCommandResult>;

public class Reset2FaHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<Reset2FaCommand, StaffCommandResult>
{
    public async Task<StaffCommandResult> Handle(Reset2FaCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reason)) return new(false, "REASON_REQUIRED");

        var target = await db.Staff.FirstOrDefaultAsync(s => s.Id == cmd.TargetId, ct);
        if (target is null) return new(false, "STAFF_NOT_FOUND");

        var rankError = StaffGuard.Check(cmd.ActorId, cmd.ActorRoleKey, target.Id, target.RoleKey);
        if (rankError is not null) return new(false, rankError);

        target.TwoFaEnabled = false;
        target.TwoFaSecret  = null;

        // Delete recovery codes
        var codes = await db.TwoFaRecoveryCodes
            .Where(c => c.StaffId == cmd.TargetId).ToListAsync(ct);
        db.TwoFaRecoveryCodes.RemoveRange(codes);

        await audit.RecordAsync(cmd.ActorId, cmd.ActorName, AuditAction.Staff2faReset,
            "Staff", cmd.TargetId, reason: cmd.Reason, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/staff/{id}/sessions/revoke ────────────────────────────────────
// Sign them out everywhere, immediately.

public record RevokeStaffSessionsCommand(string TargetId, string ActorId,
    string ActorRoleKey, string ActorName) : IRequest<StaffCommandResult>;

public class RevokeStaffSessionsHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<RevokeStaffSessionsCommand, StaffCommandResult>
{
    public async Task<StaffCommandResult> Handle(RevokeStaffSessionsCommand cmd, CancellationToken ct)
    {
        var target = await db.Staff.FirstOrDefaultAsync(s => s.Id == cmd.TargetId, ct);
        if (target is null) return new(false, "STAFF_NOT_FOUND");

        var rankError = StaffGuard.Check(cmd.ActorId, cmd.ActorRoleKey, target.Id, target.RoleKey);
        if (rankError is not null) return new(false, rankError);

        var tokens = await db.StaffRefreshTokens
            .Where(t => t.StaffId == cmd.TargetId && !t.IsRevoked).ToListAsync(ct);
        foreach (var t in tokens) t.IsRevoked = true;

        await audit.RecordAsync(cmd.ActorId, cmd.ActorName, AuditAction.StaffRoleChange,
            "Staff", cmd.TargetId, reason: "Sessions revoked", ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null, new { revoked_count = tokens.Count });
    }
}

// ── POST /admin/staff/{id}/suspend ────────────────────────────────────────────

public record SuspendStaffCommand(string TargetId, string ActorId,
    string ActorRoleKey, string ActorName, string Reason, DateTime? Until)
    : IRequest<StaffCommandResult>;

public class SuspendStaffHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<SuspendStaffCommand, StaffCommandResult>
{
    public async Task<StaffCommandResult> Handle(SuspendStaffCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reason)) return new(false, "REASON_REQUIRED");

        var target = await db.Staff.FirstOrDefaultAsync(s => s.Id == cmd.TargetId, ct);
        if (target is null) return new(false, "STAFF_NOT_FOUND");

        var rankError = StaffGuard.Check(cmd.ActorId, cmd.ActorRoleKey, target.Id, target.RoleKey);
        if (rankError is not null) return new(false, rankError);

        if (target.RoleKey == "super_admin")
        {
            var lastError = await StaffGuard.LastSuperAdminCheckAsync(db, target.Id, ct);
            if (lastError is not null) return new(false, lastError);
        }

        var before = new { target.Status };
        target.Status            = StaffStatus.Suspended;
        target.SuspensionReason  = cmd.Reason;
        target.SuspendedUntil    = cmd.Until;

        await audit.RecordAsync(cmd.ActorId, cmd.ActorName, AuditAction.StaffSuspend,
            "Staff", cmd.TargetId, reason: cmd.Reason,
            before: before, after: new { Status = "suspended", Until = cmd.Until }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/staff/{id}/block ──────────────────────────────────────────────
// Revokes sessions, refuses sign-in, invalidates pending invites and reset tokens.

public record BlockStaffCommand(string TargetId, string ActorId,
    string ActorRoleKey, string ActorName, string Reason) : IRequest<StaffCommandResult>;

public class BlockStaffHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<BlockStaffCommand, StaffCommandResult>
{
    public async Task<StaffCommandResult> Handle(BlockStaffCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reason)) return new(false, "REASON_REQUIRED");

        var target = await db.Staff.Include(s => s.RefreshTokens)
            .FirstOrDefaultAsync(s => s.Id == cmd.TargetId, ct);
        if (target is null) return new(false, "STAFF_NOT_FOUND");

        var rankError = StaffGuard.Check(cmd.ActorId, cmd.ActorRoleKey, target.Id, target.RoleKey);
        if (rankError is not null) return new(false, rankError);

        if (target.RoleKey == "super_admin")
        {
            var lastError = await StaffGuard.LastSuperAdminCheckAsync(db, target.Id, ct);
            if (lastError is not null) return new(false, lastError);
        }

        var before = new { target.Status };
        target.Status           = StaffStatus.Blocked;
        target.SuspensionReason = cmd.Reason;
        foreach (var rt in target.RefreshTokens) rt.IsRevoked = true;

        // Invalidate pending invites and password resets
        var invites = await db.StaffInvites
            .Where(i => i.Email == target.Email && !i.IsUsed).ToListAsync(ct);
        foreach (var i in invites) i.IsUsed = true;

        var resets = await db.StaffPasswordResets
            .Where(r => r.StaffId == target.Id && !r.IsUsed).ToListAsync(ct);
        foreach (var r in resets) r.IsUsed = true;

        await audit.RecordAsync(cmd.ActorId, cmd.ActorName, AuditAction.StaffBlock,
            "Staff", cmd.TargetId, reason: cmd.Reason,
            before: before, after: new { Status = "blocked" }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/staff/{id}/reinstate ──────────────────────────────────────────
// Restore access; forces password reset and 2FA re-enrolment on return.

public record ReinstateStaffCommand(string TargetId, string ActorId,
    string ActorRoleKey, string ActorName, string Reason) : IRequest<StaffCommandResult>;

public class ReinstateStaffHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<ReinstateStaffCommand, StaffCommandResult>
{
    public async Task<StaffCommandResult> Handle(ReinstateStaffCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Reason)) return new(false, "REASON_REQUIRED");

        var target = await db.Staff.FirstOrDefaultAsync(s => s.Id == cmd.TargetId, ct);
        if (target is null) return new(false, "STAFF_NOT_FOUND");

        var rankError = StaffGuard.Check(cmd.ActorId, cmd.ActorRoleKey, target.Id, target.RoleKey);
        if (rankError is not null) return new(false, rankError);

        var before = new { target.Status };
        target.Status            = StaffStatus.Active;
        target.SuspensionReason  = null;
        target.SuspendedUntil    = null;
        target.MustChangePassword = true;
        target.TwoFaEnabled      = false;
        target.TwoFaSecret       = null;

        await audit.RecordAsync(cmd.ActorId, cmd.ActorName, AuditAction.StaffReinstate,
            "Staff", cmd.TargetId, reason: cmd.Reason,
            before: before, after: new { Status = "active" }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── DELETE /admin/staff/{id} ──────────────────────────────────────────────────
// Only for accounts created in error. Refused if any audit entries exist.

public record DeleteStaffCommand(string TargetId, string ActorId,
    string ActorRoleKey, string ActorName) : IRequest<StaffCommandResult>;

public class DeleteStaffHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<DeleteStaffCommand, StaffCommandResult>
{
    public async Task<StaffCommandResult> Handle(DeleteStaffCommand cmd, CancellationToken ct)
    {
        var target = await db.Staff.FirstOrDefaultAsync(s => s.Id == cmd.TargetId, ct);
        if (target is null) return new(false, "STAFF_NOT_FOUND");

        var rankError = StaffGuard.Check(cmd.ActorId, cmd.ActorRoleKey, target.Id, target.RoleKey);
        if (rankError is not null) return new(false, rankError);

        var hasHistory = await db.AuditLogs.AnyAsync(a => a.ActorId == cmd.TargetId, ct);
        if (hasHistory) return new(false, "HAS_AUDIT_HISTORY");

        db.Staff.Remove(target);

        await audit.RecordAsync(cmd.ActorId, cmd.ActorName, AuditAction.StaffBlock,
            "Staff", cmd.TargetId, reason: "Deleted — account created in error", ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── PUT /admin/roles/{key} ────────────────────────────────────────────────────
// Edit a role's permission set. Rank is immutable. Affects everyone holding it.

public record UpdateRolePermissionsCommand(string RoleKey, string[] Permissions,
    string Reason, string ActorId, string ActorRoleKey, string ActorName)
    : IRequest<StaffCommandResult>;

public class UpdateRolePermissionsHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UpdateRolePermissionsCommand, StaffCommandResult>
{
    public async Task<StaffCommandResult> Handle(UpdateRolePermissionsCommand cmd, CancellationToken ct)
    {
        if (!AdminRoles.Roles.TryGetValue(cmd.RoleKey, out var role))
            return new(false, "ROLE_NOT_FOUND");

        var actorRank  = StaffGuard.RankOf(cmd.ActorRoleKey);
        // Actor cannot edit a role at or above their own rank
        if (actorRank <= role.Rank && cmd.ActorRoleKey != "super_admin")
            return new(false, "INSUFFICIENT_RANK");

        if (string.IsNullOrWhiteSpace(cmd.Reason))
            return new(false, "REASON_REQUIRED");

        var memberCount = await db.Staff.CountAsync(s => s.RoleKey == cmd.RoleKey, ct);

        var existing = await db.StaffRoleCustomizations
            .FirstOrDefaultAsync(r => r.RoleKey == cmd.RoleKey, ct);

        var before = existing?.PermissionsJson;
        var json   = JsonSerializer.Serialize(cmd.Permissions);

        if (existing is null)
        {
            db.StaffRoleCustomizations.Add(new StaffRoleCustomization
            {
                RoleKey             = cmd.RoleKey,
                PermissionsJson     = json,
                UpdatedByStaffId    = cmd.ActorId
            });
        }
        else
        {
            existing.PermissionsJson  = json;
            existing.UpdatedByStaffId = cmd.ActorId;
            existing.UpdatedAt        = DateTime.UtcNow;
        }

        await audit.RecordAsync(cmd.ActorId, cmd.ActorName, AuditAction.StaffRoleChange,
            "StaffRole", cmd.RoleKey, reason: cmd.Reason,
            before: before, after: json, ct: ct);
        await db.SaveChangesAsync(ct);

        return new(true, null, new
        {
            role_key     = cmd.RoleKey,
            permissions  = cmd.Permissions,
            member_count = memberCount
        });
    }
}
