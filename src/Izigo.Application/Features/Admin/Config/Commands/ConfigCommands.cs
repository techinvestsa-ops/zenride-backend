using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Admin.Config.Dtos;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Config.Commands;

// ── UPDATE DISPATCH CONFIG ────────────────────────────────────────────────────────

public record UpdateDispatchConfigCommand(
    string Market,
    int OfferTimeoutSeconds,
    int SearchRadiusM,
    string Strategy,
    int MaxConcurrentOffers,
    int LocationPingOnTripS,
    int LocationPingIdleS,
    string Reason,
    string StaffId,
    string StaffName) : IRequest<DispatchConfigDto>;

public class UpdateDispatchConfigHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UpdateDispatchConfigCommand, DispatchConfigDto>
{
    public async Task<DispatchConfigDto> Handle(UpdateDispatchConfigCommand cmd, CancellationToken ct)
    {
        var cfg = await db.DispatchConfigs
            .FirstOrDefaultAsync(d => d.Market == cmd.Market, ct);

        object? before = cfg is not null ? new
        {
            cfg.OfferTimeoutSeconds, cfg.SearchRadiusM, cfg.Strategy,
            cfg.MaxConcurrentOffers, cfg.LocationPingOnTripS, cfg.LocationPingIdleS
        } : null;

        if (cfg is null)
        {
            cfg = new Domain.Entities.DispatchConfig { Market = cmd.Market };
            db.DispatchConfigs.Add(cfg);
        }

        cfg.OfferTimeoutSeconds = cmd.OfferTimeoutSeconds;
        cfg.SearchRadiusM       = cmd.SearchRadiusM;
        cfg.Strategy            = cmd.Strategy;
        cfg.MaxConcurrentOffers = cmd.MaxConcurrentOffers;
        cfg.LocationPingOnTripS = cmd.LocationPingOnTripS;
        cfg.LocationPingIdleS   = cmd.LocationPingIdleS;
        cfg.UpdatedByStaffId    = cmd.StaffId;
        cfg.UpdatedAt           = DateTime.UtcNow;

        var after = new
        {
            cfg.OfferTimeoutSeconds, cfg.SearchRadiusM, cfg.Strategy,
            cfg.MaxConcurrentOffers, cfg.LocationPingOnTripS, cfg.LocationPingIdleS
        };

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.ConfigChange,
            "DispatchConfig", cmd.Market, cmd.Reason, before, after, ct: ct);

        await db.SaveChangesAsync(ct);

        return new DispatchConfigDto(cfg.Market, cfg.OfferTimeoutSeconds, cfg.SearchRadiusM,
            cfg.Strategy, cfg.MaxConcurrentOffers, cfg.LocationPingOnTripS, cfg.LocationPingIdleS,
            cfg.UpdatedByStaffId, cfg.UpdatedAt);
    }
}

// ── UPDATE FEATURE FLAG ───────────────────────────────────────────────────────────

public record UpdateFeatureFlagCommand(
    string Key,
    bool Enabled,
    int? RolloutPct,
    string? Scope,
    string Reason,
    string Market,
    string StaffId,
    string StaffName) : IRequest<FeatureFlagResult>;

public record FeatureFlagResult(bool Success, string? ErrorCode, Domain.Entities.FeatureFlag? Flag);

public class UpdateFeatureFlagHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UpdateFeatureFlagCommand, FeatureFlagResult>
{
    public async Task<FeatureFlagResult> Handle(UpdateFeatureFlagCommand cmd, CancellationToken ct)
    {
        var flag = await db.FeatureFlags
            .FirstOrDefaultAsync(f => f.Key == cmd.Key && f.Market == cmd.Market, ct);

        object? before = flag is not null ? new { flag.Enabled, flag.RolloutPct, flag.Scope } : null;

        if (flag is null)
        {
            flag = new Domain.Entities.FeatureFlag
            {
                Key    = cmd.Key,
                Market = cmd.Market,
                Description = cmd.Key   // will be updated if needed
            };
            db.FeatureFlags.Add(flag);
        }

        flag.Enabled    = cmd.Enabled;
        flag.RolloutPct = cmd.RolloutPct ?? flag.RolloutPct;
        flag.Scope      = cmd.Scope ?? flag.Scope;
        flag.UpdatedAt  = DateTime.UtcNow;
        flag.UpdatedByStaffId = cmd.StaffId;

        var after = new { flag.Enabled, flag.RolloutPct, flag.Scope };

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.FeatureFlagChange,
            "FeatureFlag", cmd.Key, cmd.Reason, before, after, ct: ct);

        await db.SaveChangesAsync(ct);
        return new(true, null, flag);
    }
}

// ── UPDATE MAINTENANCE ────────────────────────────────────────────────────────────

public record UpdateMaintenanceCommand(
    string Mode,
    string? Message,
    string? App,
    string Reason,
    string Market,
    string StaffId,
    string StaffName) : IRequest;

public class UpdateMaintenanceHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UpdateMaintenanceCommand>
{
    public async Task Handle(UpdateMaintenanceCommand cmd, CancellationToken ct)
    {
        var config = await db.PlatformConfigs
            .FirstOrDefaultAsync(c => c.Market == cmd.Market, ct);

        object? before = config is not null
            ? new { config.MaintenanceModeRider, config.MaintenanceModeDriver, config.MaintenanceMessage } : null;

        if (config is not null)
        {
            var isMaintenance = cmd.Mode == "maintenance";
            var isReadOnly    = cmd.Mode == "read_only";

            if (cmd.App is null or "all" or "rider" or "both")
                config.MaintenanceModeRider  = isMaintenance || isReadOnly;
            if (cmd.App is null or "all" or "driver" or "both")
                config.MaintenanceModeDriver = isMaintenance || isReadOnly;
            config.MaintenanceMessage = cmd.Message;
        }

        var after = new { Mode = cmd.Mode, cmd.Message };
        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.MaintenanceChange,
            "PlatformConfig", cmd.Market, cmd.Reason, before, after, ct: ct);

        await db.SaveChangesAsync(ct);
    }
}

// ── UPDATE INTEGRATION ────────────────────────────────────────────────────────────

public record UpdateIntegrationCommand(
    string Key,
    string ApiKey,
    string Reason,
    string Market,
    string StaffId,
    string StaffName) : IRequest<bool>;

public class UpdateIntegrationHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UpdateIntegrationCommand, bool>
{
    public async Task<bool> Handle(UpdateIntegrationCommand cmd, CancellationToken ct)
    {
        var integration = await db.Integrations
            .FirstOrDefaultAsync(i => i.Key == cmd.Key && i.Market == cmd.Market, ct);

        if (integration is null)
        {
            integration = new Domain.Entities.Integration
            {
                Key    = cmd.Key,
                Label  = cmd.Key,
                Market = cmd.Market
            };
            db.Integrations.Add(integration);
        }

        // Never log the actual secret
        integration.KeyLastRotatedAt = DateTime.UtcNow;
        integration.IsConnected      = true;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.IntegrationChange,
            "Integration", cmd.Key, cmd.Reason, null, new { rotated_at = DateTime.UtcNow }, ct: ct);

        await db.SaveChangesAsync(ct);
        return true;
    }
}
