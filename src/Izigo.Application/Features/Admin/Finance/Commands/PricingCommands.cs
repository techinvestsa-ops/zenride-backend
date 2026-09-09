using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Finance.Commands;

// ── PUT /admin/pricing/fare-rules/{class} ─────────────────────────────────────
// Creates a new version, never mutates. Permission: pricing.write.
// In-flight trips keep the version they were quoted on.

public record UpdateFareRuleCommand(string Market, string ServiceClass,
    long Base, long PerKm, long PerMin, long Minimum, long WaitingPerMin,
    long CancellationFee, DateTime? EffectiveFrom,
    string StaffId, string StaffName) : IRequest<FinanceCommandResult>;

public class UpdateFareRuleHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UpdateFareRuleCommand, FinanceCommandResult>
{
    public async Task<FinanceCommandResult> Handle(UpdateFareRuleCommand cmd, CancellationToken ct)
    {
        if (!Enum.TryParse<ServiceClass>(cmd.ServiceClass, true, out var sc))
            return new(false, "INVALID_SERVICE_CLASS");

        // Get current version
        var current = await db.FareRules
            .Where(r => r.Market == cmd.Market && r.ServiceClass == sc && r.IsActive)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(ct);

        var before = current is not null
            ? (object)new { current.Base, current.PerKm, current.PerMin, current.Minimum }
            : null;

        // Deactivate the current rule
        if (current is not null) current.IsActive = false;

        // Create new version
        var nextVersion = (current?.Version ?? 0) + 1;
        var newRule = new Domain.Entities.FareRule
        {
            ServiceClass     = sc,
            Base             = cmd.Base,
            PerKm            = cmd.PerKm,
            PerMin           = cmd.PerMin,
            Minimum          = cmd.Minimum,
            WaitingPerMin    = cmd.WaitingPerMin,
            CancellationFee  = cmd.CancellationFee,
            IsActive         = true,
            Version          = nextVersion,
            EffectiveFrom    = cmd.EffectiveFrom ?? DateTime.UtcNow,
            UpdatedByStaffId = cmd.StaffId,
            Market           = cmd.Market
        };
        db.FareRules.Add(newRule);

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.FareRuleChange,
            "FareRule", newRule.Id,
            reason: $"Fare rule updated for {cmd.ServiceClass} (v{nextVersion})",
            before: before,
            after: new { cmd.Base, cmd.PerKm, cmd.PerMin, cmd.Minimum, Version = nextVersion },
            market: cmd.Market, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null, new { id = newRule.Id, version = nextVersion });
    }
}

// ── PUT /admin/pricing/commission ─────────────────────────────────────────────

public record UpdateCommissionCommand(string Market, decimal CommissionRate, decimal BonusRate,
    string? BonusLabel, long? CashSettlementCap, string? VerticalOverridesJson,
    DateTime? EffectiveFrom, string StaffId, string StaffName) : IRequest<FinanceCommandResult>;

public class UpdateCommissionHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UpdateCommissionCommand, FinanceCommandResult>
{
    public async Task<FinanceCommandResult> Handle(UpdateCommissionCommand cmd, CancellationToken ct)
    {
        var current = await db.CommissionConfigs
            .Where(c => c.Market == cmd.Market)
            .OrderByDescending(c => c.Version)
            .FirstOrDefaultAsync(ct);

        var before = current is not null
            ? (object)new { current.CommissionRate, current.BonusRate, current.CashSettlementCap }
            : null;

        var nextVersion = (current?.Version ?? 0) + 1;
        var newConfig = new Domain.Entities.CommissionConfig
        {
            CommissionRate       = cmd.CommissionRate,
            BonusRate            = cmd.BonusRate,
            BonusLabel           = cmd.BonusLabel ?? current?.BonusLabel ?? "Bonus",
            CashSettlementCap    = cmd.CashSettlementCap ?? current?.CashSettlementCap ?? 10_000,
            VerticalOverridesJson = cmd.VerticalOverridesJson ?? current?.VerticalOverridesJson ?? "{}",
            Version              = nextVersion,
            EffectiveFrom        = cmd.EffectiveFrom ?? DateTime.UtcNow,
            UpdatedByStaffId     = cmd.StaffId,
            Market               = cmd.Market
        };
        db.CommissionConfigs.Add(newConfig);

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.CommissionChange,
            "CommissionConfig", newConfig.Id,
            reason: $"Commission updated v{nextVersion}",
            before: before,
            after: new { cmd.CommissionRate, cmd.BonusRate, Version = nextVersion },
            market: cmd.Market, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null, new { version = nextVersion });
    }
}

// ── PUT /admin/pricing/surge/{zone_id} ───────────────────────────────────────
// multiplier, expires_at, reason. Above 1.6× requires second approver.

public record UpdateSurgeCommand(string ZoneId, decimal Multiplier, DateTime? ExpiresAt,
    string Reason, string StaffId, string StaffName, string Market) : IRequest<FinanceCommandResult>;

public class UpdateSurgeHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UpdateSurgeCommand, FinanceCommandResult>
{
    public async Task<FinanceCommandResult> Handle(UpdateSurgeCommand cmd, CancellationToken ct)
    {
        var zone = await db.Zones.AnyAsync(z => z.Id == cmd.ZoneId, ct);
        if (!zone) return new(false, "ZONE_NOT_FOUND");

        // Above 1.6× requires a second approver (spec: "should require a second approver")
        if (cmd.Multiplier > 1.6m)
            return new(false, "SECOND_APPROVER_REQUIRED");

        var existing = await db.SurgeZones.FirstOrDefaultAsync(s => s.ZoneId == cmd.ZoneId, ct);
        var before   = existing is not null ? (object)new { existing.Multiplier, existing.IsActive } : null;

        if (existing is null)
        {
            existing = new Domain.Entities.SurgeZone { ZoneId = cmd.ZoneId };
            db.SurgeZones.Add(existing);
        }

        existing.Multiplier           = cmd.Multiplier;
        existing.IsAutomatic          = false; // manual override
        existing.IsActive             = cmd.Multiplier > 1.0m;
        existing.ExpiresAt            = cmd.ExpiresAt;
        existing.Reason               = cmd.Reason;
        existing.OverriddenByStaffId  = cmd.StaffId;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.SurgeOverride,
            "SurgeZone", cmd.ZoneId, cmd.Reason, before,
            new { cmd.Multiplier, cmd.ExpiresAt }, market: cmd.Market, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}
