using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Application.Features.Admin.Auth;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace Izigo.Application.Features.Admin.StaffManagement.Queries;

// ── Helpers ───────────────────────────────────────────────────────────────────

file static class StaffQueryHelpers
{
    public static int RankOf(string roleKey)
        => AdminRoles.Roles.TryGetValue(roleKey, out var r) ? r.Rank : 0;
}

// ── GET /admin/staff ──────────────────────────────────────────────────────────
// can_manage is server-computed: actor.rank > target.rank.

public record StaffRowDto(string Id, string Name, string Email, string? Phone,
    string RoleKey, string RoleLabel, int RoleRank,
    string Status, int PermissionCount, int OverrideCount,
    bool TwoFaEnabled, List<string> Markets,
    DateTime? LastActiveAt, bool CanManage);

public record GetStaffListQuery(string ActorId, string? Status, string? Role,
    string? Market, bool? TwoFa, string? Q, int Page, int PerPage) : IRequest<object>;

public class GetStaffListHandler(IApplicationDbContext db)
    : IRequestHandler<GetStaffListQuery, object>
{
    public async Task<object> Handle(GetStaffListQuery req, CancellationToken ct)
    {
        var actor = await db.Staff
            .Where(s => s.Id == req.ActorId)
            .Select(s => new { s.RoleKey })
            .FirstOrDefaultAsync(ct);

        var actorRank = actor is not null ? StaffQueryHelpers.RankOf(actor.RoleKey) : 0;

        var query = db.Staff.AsQueryable();

        if (!string.IsNullOrWhiteSpace(req.Status) &&
            Enum.TryParse<StaffStatus>(req.Status, true, out var parsedStatus))
            query = query.Where(s => s.Status == parsedStatus);

        if (!string.IsNullOrWhiteSpace(req.Role))
            query = query.Where(s => s.RoleKey == req.Role);

        if (!string.IsNullOrWhiteSpace(req.Market))
            query = query.Where(s => s.Markets.Contains(req.Market));

        if (req.TwoFa.HasValue)
            query = query.Where(s => s.TwoFaEnabled == req.TwoFa.Value);

        if (!string.IsNullOrWhiteSpace(req.Q))
        {
            var q = req.Q.ToLower();
            query = query.Where(s => s.Name.ToLower().Contains(q) ||
                                     s.Email.ToLower().Contains(q));
        }

        var perPage  = Math.Max(1, Math.Min(100, req.PerPage));
        var total    = await query.CountAsync(ct);
        var lastPage = (int)Math.Ceiling((double)total / perPage);

        var staff = await query
            .OrderBy(s => s.Name)
            .Skip((req.Page - 1) * perPage).Take(perPage)
            .Select(s => new
            {
                s.Id, s.Name, s.Email, s.Phone, s.RoleKey,
                s.Status, s.TwoFaEnabled, s.Markets, s.LastActiveAt,
                s.GrantedPermissions, s.RevokedPermissions
            })
            .ToListAsync(ct);

        var rows = staff.Select(s =>
        {
            AdminRoles.Roles.TryGetValue(s.RoleKey, out var role);
            var basePerms  = role?.Permissions ?? [];
            var effective  = basePerms.Union(s.GrantedPermissions).Except(s.RevokedPermissions).ToArray();
            var targetRank = role?.Rank ?? 0;
            return new StaffRowDto(
                s.Id, s.Name, s.Email, s.Phone,
                s.RoleKey, role?.Label ?? s.RoleKey, targetRank,
                s.Status.ToString().ToLower(),
                effective.Length, s.GrantedPermissions.Count + s.RevokedPermissions.Count,
                s.TwoFaEnabled, s.Markets, s.LastActiveAt,
                CanManage: actorRank > targetRank);
        }).ToArray();

        var facets = new Dictionary<string, Dictionary<string, int>>
        {
            ["status"] = staff.GroupBy(s => s.Status.ToString().ToLower())
                             .ToDictionary(g => g.Key, g => g.Count()),
            ["role"]   = staff.GroupBy(s => s.RoleKey)
                             .ToDictionary(g => g.Key, g => g.Count())
        };

        return AdminApiResponse.Ok(rows, new AdminPagedMeta(req.Page, perPage, total, lastPage, facets));
    }
}

// ── GET /admin/staff/{id} ─────────────────────────────────────────────────────
// Detail: resolved permissions, overrides, session list, last 50 audit entries.

public record GetStaffDetailQuery(string TargetId) : IRequest<object?>;

public class GetStaffDetailHandler(IApplicationDbContext db)
    : IRequestHandler<GetStaffDetailQuery, object?>
{
    public async Task<object?> Handle(GetStaffDetailQuery req, CancellationToken ct)
    {
        var staff = await db.Staff
            .Where(s => s.Id == req.TargetId)
            .Select(s => new
            {
                s.Id, s.Name, s.Email, s.Phone, s.RoleKey, s.Status,
                s.TwoFaEnabled, s.MustChangePassword, s.Markets,
                s.GrantedPermissions, s.RevokedPermissions,
                s.LastActiveAt, s.SuspensionReason, s.SuspendedUntil, s.CreatedAt
            })
            .FirstOrDefaultAsync(ct);

        if (staff is null) return null;

        AdminRoles.Roles.TryGetValue(staff.RoleKey, out var role);
        var effective = AdminRoles.GetEffectivePermissions(
            new Domain.Entities.Staff
            {
                RoleKey              = staff.RoleKey,
                GrantedPermissions   = staff.GrantedPermissions,
                RevokedPermissions   = staff.RevokedPermissions
            });

        var sessions = await db.StaffRefreshTokens
            .Where(t => t.StaffId == req.TargetId && !t.IsRevoked && t.ExpiresAt > DateTime.UtcNow)
            .Select(t => new { t.Id, t.DeviceInfo, t.IpAddress, t.AbsoluteCreatedAt, t.ExpiresAt, t.SessionToken })
            .ToListAsync(ct);

        var auditEntries = await db.AuditLogs
            .Where(a => a.ActorId == req.TargetId)
            .OrderByDescending(a => a.CreatedAt)
            .Take(50)
            .Select(a => new { a.Id, a.Action, a.TargetType, a.TargetId, a.Reason, a.CreatedAt })
            .ToListAsync(ct);

        return new
        {
            success = true,
            data = new
            {
                id                   = staff.Id,
                name                 = staff.Name,
                email                = staff.Email,
                phone                = staff.Phone,
                role                 = new { key = staff.RoleKey, label = role?.Label, rank = role?.Rank ?? 0 },
                status               = staff.Status.ToString().ToLower(),
                twofa_enabled        = staff.TwoFaEnabled,
                must_change_password = staff.MustChangePassword,
                markets              = staff.Markets,
                last_active_at       = staff.LastActiveAt,
                suspension_reason    = staff.SuspensionReason,
                suspended_until      = staff.SuspendedUntil,
                permissions          = effective,
                permission_overrides = new
                {
                    granted = staff.GrantedPermissions,
                    revoked = staff.RevokedPermissions
                },
                sessions             = sessions,
                recent_audit         = auditEntries,
                created_at           = staff.CreatedAt
            }
        };
    }
}

// ── GET /admin/roles ──────────────────────────────────────────────────────────
// All roles with member_count, assignable_by_me.

public record GetRolesQuery(string ActorRoleKey) : IRequest<object>;

public class GetRolesHandler(IApplicationDbContext db)
    : IRequestHandler<GetRolesQuery, object>
{
    public async Task<object> Handle(GetRolesQuery req, CancellationToken ct)
    {
        var actorRank = StaffQueryHelpers.RankOf(req.ActorRoleKey);

        var memberCounts = await db.Staff
            .Where(s => s.Status == StaffStatus.Active)
            .GroupBy(s => s.RoleKey)
            .Select(g => new { RoleKey = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RoleKey, x => x.Count, ct);

        // Merge with any customised permissions stored in DB
        var customizations = await db.StaffRoleCustomizations
            .ToDictionaryAsync(r => r.RoleKey, r => r.PermissionsJson, ct);

        var roles = AdminRoles.Roles.Values.Select(r =>
        {
            var perms = customizations.TryGetValue(r.Key, out var json)
                ? JsonSerializer.Deserialize<string[]>(json) ?? r.Permissions
                : r.Permissions;

            return (object)new
            {
                key              = r.Key,
                label            = r.Label,
                rank             = r.Rank,
                permissions      = perms,
                permission_count = perms.Length,
                member_count     = memberCounts.TryGetValue(r.Key, out var cnt) ? cnt : 0,
                assignable_by_me = actorRank > r.Rank
            };
        }).ToList();

        return new { success = true, data = roles };
    }
}

// ── GET /admin/permissions ────────────────────────────────────────────────────
// Full permission catalogue. Console renders its editor from this.

public record GetPermissionCatalogueQuery : IRequest<object>;

public class GetPermissionCatalogueHandler : IRequestHandler<GetPermissionCatalogueQuery, object>
{
    private static readonly (string Key, string Label, string Group)[] Catalogue =
    [
        ("dashboard.view",       "View dashboard",             "overview"),
        ("trips.view",           "View trips",                 "trips"),
        ("trips.refund",         "Refund trips",               "trips"),
        ("trips.adjust",         "Adjust trip fares",          "trips"),
        ("ops.view",             "View live ops",              "operations"),
        ("ops.write",            "Control live ops",           "operations"),
        ("riders.view",          "View riders",                "people"),
        ("riders.edit",          "Edit rider profiles",        "people"),
        ("riders.moderate",      "Moderate riders",            "people"),
        ("drivers.view",         "View drivers",               "people"),
        ("drivers.edit",         "Edit driver profiles",       "people"),
        ("drivers.moderate",     "Moderate drivers",           "people"),
        ("kyc.view",             "View KYC applications",      "kyc"),
        ("kyc.approve",          "Approve / reject KYC",       "kyc"),
        ("moderation.view",      "View moderation log",        "moderation"),
        ("moderation.write",     "Take moderation actions",    "moderation"),
        ("payments.view",        "View payments",              "money"),
        ("payments.write",       "Manage payments",            "money"),
        ("wallets.view",         "View wallets",               "money"),
        ("wallets.adjust",       "Adjust wallet balances",     "money"),
        ("wallets.freeze",       "Freeze wallets",             "money"),
        ("payouts.view",         "View payouts",               "money"),
        ("payouts.approve",      "Approve payouts",            "money"),
        ("pricing.view",         "View pricing rules",         "money"),
        ("pricing.write",        "Edit pricing rules",         "money"),
        ("recon.view",           "View reconciliation",        "money"),
        ("promotions.view",      "View promotions",            "growth"),
        ("promotions.write",     "Manage promotions",          "growth"),
        ("support.view",         "View support tickets",       "growth"),
        ("support.write",        "Manage support tickets",     "growth"),
        ("broadcasts.view",      "View broadcasts",            "growth"),
        ("broadcasts.send",      "Send broadcasts",            "growth"),
        ("safety.view",          "View safety incidents",      "safety"),
        ("safety.write",         "Manage safety incidents",    "safety"),
        ("zones.view",           "View zones",                 "platform"),
        ("zones.write",          "Manage zones",               "platform"),
        ("content.view",         "View content",               "platform"),
        ("content.write",        "Edit content",               "platform"),
        ("staff.manage",         "Manage staff",               "staff"),
        ("staff.password",       "Reset staff passwords",      "staff"),
        ("staff.revoke",         "Suspend / block staff",      "staff"),
        ("audit.view",           "View audit log",             "audit"),
        ("config.view",          "View platform config",       "config"),
        ("config.write",         "Edit platform config",       "config"),
        ("integrations.view",    "View integrations",          "config"),
        ("integrations.write",   "Edit integrations",          "config")
    ];

    public Task<object> Handle(GetPermissionCatalogueQuery req, CancellationToken ct)
    {
        var data = Catalogue.Select(p => new { key = p.Key, label = p.Label, group = p.Group }).ToList();
        return Task.FromResult<object>(new { success = true, data });
    }
}
