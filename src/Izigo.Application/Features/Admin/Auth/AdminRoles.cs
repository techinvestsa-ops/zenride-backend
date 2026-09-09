using Izigo.Domain.Entities;

namespace Izigo.Application.Features.Admin.Auth;

public record AdminRoleDefinition(string Key, string Label, int Rank, string[] Permissions);

public static class AdminRoles
{
    // ── All 43 permissions ──────────────────────────────────────────────────────
    public static readonly string[] AllPermissions =
    [
        "dashboard.view",
        "trips.view", "trips.refund", "trips.adjust",
        "ops.view", "ops.write",
        "riders.view", "riders.edit", "riders.moderate",
        "drivers.view", "drivers.edit", "drivers.moderate",
        "kyc.view", "kyc.approve",
        "moderation.view", "moderation.write",
        "payments.view", "payments.write",
        "wallets.view", "wallets.adjust", "wallets.freeze",
        "payouts.view", "payouts.approve",
        "pricing.view", "pricing.write",
        "recon.view",
        "promotions.view", "promotions.write",
        "support.view", "support.write",
        "broadcasts.view", "broadcasts.send",
        "safety.view", "safety.write",
        "zones.view", "zones.write",
        "content.view", "content.write",
        "staff.manage", "staff.password", "staff.revoke",
        "audit.view",
        "config.view", "config.write",
        "integrations.view", "integrations.write"
    ];

    /// <summary>Permissions that require 2FA to exercise.</summary>
    public static readonly HashSet<string> MoneyPermissions =
    [
        "wallets.adjust",
        "payouts.approve",
        "pricing.write",
        "trips.refund"
    ];

    // ── Role definitions ────────────────────────────────────────────────────────
    public static readonly Dictionary<string, AdminRoleDefinition> Roles = new()
    {
        ["super_admin"] = new("super_admin", "Super admin", 100, AllPermissions),

        ["admin"] = new("admin", "Admin", 90,
            AllPermissions.Except(["staff.manage", "config.write", "integrations.write"]).ToArray()),

        ["operations"] = new("operations", "Operations", 60,
        [
            "dashboard.view",
            "trips.view", "trips.refund",
            "ops.view", "ops.write",
            "riders.view", "drivers.view",
            "safety.view", "safety.write"
        ]),

        ["finance"] = new("finance", "Finance", 60,
        [
            "dashboard.view",
            "trips.view",
            "payments.view", "payments.write",
            "wallets.view", "wallets.adjust",
            "payouts.view", "payouts.approve",
            "pricing.view", "pricing.write",
            "recon.view"
        ]),

        ["compliance"] = new("compliance", "Compliance", 50,
        [
            "dashboard.view",
            "riders.view", "drivers.view",
            "kyc.view", "kyc.approve",
            "audit.view",
            "moderation.view"
        ]),

        ["support"] = new("support", "Support", 40,
        [
            "dashboard.view",
            "trips.view",
            "riders.view", "drivers.view",
            "support.view", "support.write",
            "wallets.view"
        ]),

        ["read_only"] = new("read_only", "Read only", 10,
        [
            "dashboard.view",
            "trips.view",
            "riders.view", "drivers.view",
            "payments.view",
            "wallets.view",
            "payouts.view",
            "audit.view"
        ])
    };

    /// <summary>Computes effective permissions: role base + granted overrides − revoked overrides.</summary>
    public static string[] GetEffectivePermissions(Staff staff)
    {
        var basePerms = Roles.TryGetValue(staff.RoleKey, out var role)
            ? role.Permissions
            : [];

        return basePerms
            .Union(staff.GrantedPermissions)
            .Except(staff.RevokedPermissions)
            .ToArray();
    }
}
