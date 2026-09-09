using Izigo.Application.Features.Admin.AuditLog.Queries;
using Izigo.Application.Features.Admin.Config.Commands;
using Izigo.Application.Features.Admin.Config.Dtos;
using Izigo.Application.Features.Admin.Config.Queries;
using Izigo.Application.Features.Admin.Ops.Queries;
using Izigo.Application.Features.Admin.Zones.Commands;
using Izigo.Application.Features.Admin.Zones.Queries;
using Izigo.Application.Features.Realtime.Dtos;
using Izigo.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1.Admin;

/// <summary>
/// Admin platform configuration controller covering zone management, content
/// (pages, banners), app versioning, feature flags, maintenance mode, third-party
/// integration settings, audit logging, and realtime configuration.
/// </summary>
[Authorize(Policy = "AdminPolicy")]
public class PlatformAdminController : AdminBaseController
{
    // ── Zones ─────────────────────────────────────────────────────────────────

    [HttpGet("api/v1/admin/zones")]
    public async Task<IActionResult> GetZones(CancellationToken ct = default)
    {
        var check = CheckPermission("zones.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetAdminZonesQuery(Market), ct));
    }

    [HttpPost("api/v1/admin/zones")]
    public async Task<IActionResult> CreateZone(
        [FromBody] CreateZoneRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("zones.write");
        if (check is not null) return check;

        var result = await Mediator.Send(new CreateZoneCommand(
            req.Name, req.PolygonGeoJson, req.Verticals ?? [],
            req.Status ?? "planned", req.CenterLat, req.CenterLng,
            Market, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true, data = result.Data });
    }

    [HttpPut("api/v1/admin/zones/{id}")]
    public async Task<IActionResult> UpdateZone(string id,
        [FromBody] UpdateZoneRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("zones.write");
        if (check is not null) return check;

        var result = await Mediator.Send(new UpdateZoneCommand(
            id, req.Name, req.Status, req.Verticals, req.PolygonGeoJson,
            Market, StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.ErrorCode switch
        {
            null              => Ok(new { success = true }),
            "ZONE_NOT_FOUND"  => NotFound(new { success = false, error = new { code = result.ErrorCode } }),
            _                 => UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } })
        };
    }

    /// <summary>Returns all zone boundaries as a GeoJSON feature collection with a centre per zone (A24).</summary>
    [HttpGet("api/v1/admin/zones/geojson")]
    public async Task<IActionResult> GetZonesGeoJson(CancellationToken ct = default)
    {
        var check = CheckPermission("zones.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetZonesGeoJsonQuery(Market), ct));
    }

    // ── Content ───────────────────────────────────────────────────────────────

    [HttpGet("api/v1/admin/content")]
    public async Task<IActionResult> GetContent(CancellationToken ct = default)
    {
        var check = CheckPermission("content.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetAdminContentQuery(Market), ct));
    }

    [HttpPut("api/v1/admin/content/{key}")]
    public async Task<IActionResult> UpdateContent(string key,
        [FromBody] UpdateContentRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("content.write");
        if (check is not null) return check;

        var result = await Mediator.Send(new UpdateContentCommand(
            key, Market, req.Fr, req.En, req.Publish,
            StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true })
            : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });
    }

    [HttpPost("api/v1/admin/content/banners")]
    public async Task<IActionResult> UpsertBanner(
        [FromBody] CreateBannerRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("content.write");
        if (check is not null) return check;

        var result = await Mediator.Send(new CreateBannerCommand(
            req.ImageUrl, req.DeepLink, req.Placement ?? "home",
            req.StartsAt, req.EndsAt, req.Order,
            Market, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true, data = result.Data });
    }

    // ── App Config ────────────────────────────────────────────────────────────

    [HttpPut("api/v1/admin/config/versions")]
    public async Task<IActionResult> UpdateVersionConfig(
        [FromBody] UpdateVersionConfigRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("config.write");
        if (check is not null) return check;

        var result = await Mediator.Send(new UpdateVersionConfigCommand(
            req.Platform, req.Latest, req.Minimum, req.Gate ?? "none", req.StoreUrl,
            Market, StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.ErrorCode switch
        {
            null                 => Ok(new { success = true }),
            "INVALID_PLATFORM"   => BadRequest(new { success = false, error = new { code = result.ErrorCode } }),
            "INVALID_GATE"       => BadRequest(new { success = false, error = new { code = result.ErrorCode } }),
            _                    => UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } })
        };
    }

    /// <summary>Returns the current platform configuration values (config.view required).</summary>
    [HttpGet("api/v1/admin/config")]
    public async Task<IActionResult> GetConfig(CancellationToken ct)
    {
        var check = CheckPermission("config.view");
        if (check is not null) return check;

        var result = await Mediator.Send(new GetAdminConfigQuery(Market), ct);
        return Ok(result);
    }

    /// <summary>Updates the dispatch engine configuration (config.write required, audited).</summary>
    [HttpPut("api/v1/admin/config/dispatch")]
    public async Task<IActionResult> UpdateDispatchConfig(
        [FromBody] UpdateDispatchConfigRequest req, CancellationToken ct)
    {
        var check = CheckPermission("config.write");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        if (!new[] { "sequential", "broadcast" }.Contains(req.Strategy))
            return BadRequest(new { success = false, error = new { code = "INVALID_STRATEGY" } });

        var staffName = CurrentStaff.Email ?? StaffId;
        var dto = await Mediator.Send(new UpdateDispatchConfigCommand(
            Market, req.OfferTimeoutSeconds, req.SearchRadiusM, req.Strategy,
            req.MaxConcurrentOffers, req.LocationPingOnTripS, req.LocationPingIdleS,
            req.Reason, StaffId, staffName), ct);

        return Ok(new { success = true, data = dto });
    }

    // ── Feature Flags ─────────────────────────────────────────────────────────

    /// <summary>Returns all feature flag definitions and their current states (config.view required).</summary>
    [HttpGet("api/v1/admin/config/flags")]
    public async Task<IActionResult> GetFlags(CancellationToken ct)
    {
        var check = CheckPermission("config.view");
        if (check is not null) return check;

        var result = await Mediator.Send(new GetFeatureFlagsQuery(Market), ct);
        return Ok(result);
    }

    /// <summary>Enables or disables a specific feature flag (config.write required, audited).</summary>
    [HttpPut("api/v1/admin/config/flags/{key}")]
    public async Task<IActionResult> UpdateFlag(string key,
        [FromBody] UpdateFeatureFlagRequest req, CancellationToken ct)
    {
        var check = CheckPermission("config.write");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var staffName = CurrentStaff.Email ?? StaffId;
        var result = await Mediator.Send(new UpdateFeatureFlagCommand(
            key, req.Enabled, req.RolloutPct, req.Scope, req.Reason, Market, StaffId, staffName), ct);

        if (!result.Success)
            return BadRequest(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new
        {
            success = true,
            data = new
            {
                result.Flag!.Key, result.Flag.Description,
                result.Flag.Enabled, result.Flag.RolloutPct, result.Flag.Scope
            }
        });
    }

    // ── Maintenance ───────────────────────────────────────────────────────────

    /// <summary>Toggles maintenance mode on or off and sets the message shown to users (config.write required).</summary>
    [HttpPut("api/v1/admin/config/maintenance")]
    public async Task<IActionResult> UpdateMaintenance(
        [FromBody] UpdateMaintenanceRequest req, CancellationToken ct)
    {
        var check = CheckPermission("config.write");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var staffName = CurrentStaff.Email ?? StaffId;
        await Mediator.Send(new UpdateMaintenanceCommand(
            req.Mode, req.Message, req.App, req.Reason, Market, StaffId, staffName), ct);

        return Ok(new { success = true });
    }

    // ── Integrations ──────────────────────────────────────────────────────────

    /// <summary>Returns configuration and status for all third-party integrations (integrations.view required).</summary>
    [HttpGet("api/v1/admin/integrations")]
    public async Task<IActionResult> GetIntegrations(CancellationToken ct)
    {
        var check = CheckPermission("integrations.view");
        if (check is not null) return check;

        var result = await Mediator.Send(new GetIntegrationsQuery(Market), ct);
        return Ok(result);
    }

    /// <summary>Updates the credentials or settings for a specific integration (integrations.write, audited).</summary>
    [HttpPut("api/v1/admin/integrations/{key}")]
    public async Task<IActionResult> UpdateIntegration(string key,
        [FromBody] UpdateIntegrationRequest req, CancellationToken ct)
    {
        var check = CheckPermission("integrations.write");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var staffName = CurrentStaff.Email ?? StaffId;
        await Mediator.Send(new UpdateIntegrationCommand(
            key, req.ApiKey, req.Reason, Market, StaffId, staffName), ct);

        return Ok(new { success = true });
    }

    // ── Audit Log ─────────────────────────────────────────────────────────────

    /// <summary>Returns a filterable, paginated audit log of admin actions (audit.view required).</summary>
    [HttpGet("api/v1/admin/audit-log")]
    public async Task<IActionResult> GetAuditLog(
        [FromQuery] string? actor,
        [FromQuery] string? action,
        [FromQuery] string? target_type,
        [FromQuery] string? target_id,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int per_page = 25,
        CancellationToken ct = default)
    {
        var check = CheckPermission("audit.view");
        if (check is not null) return check;

        var result = await Mediator.Send(
            new GetAuditLogQuery(actor, action, target_type, target_id, from, to, q, page, per_page), ct);

        return Ok(result);
    }

    /// <summary>Returns the full details of a single audit log entry (audit.view required).</summary>
    [HttpGet("api/v1/admin/audit-log/{id}")]
    public async Task<IActionResult> GetAuditLogEntry(string id, CancellationToken ct)
    {
        var check = CheckPermission("audit.view");
        if (check is not null) return check;

        var entry = await Mediator.Send(new GetAuditLogEntryQuery(id), ct);
        if (entry is null) return NotFound(new { success = false, error = new { code = "NOT_FOUND" } });

        return Ok(new { success = true, data = entry });
    }

    /// <summary>Exports the audit log as a downloadable file. Returns 202 with job_id (audit.view required).</summary>
    [HttpGet("api/v1/admin/audit-log/export")]
    public async Task<IActionResult> ExportAuditLog(CancellationToken ct)
    {
        var check = CheckPermission("audit.view");
        if (check is not null) return check;

        var result = await Mediator.Send(new ExportAuditLogQuery(StaffId), ct);
        return Accepted(new { success = true, data = new { job_id = result.JobId, status_url = result.StatusUrl } });
    }

    // ── Realtime ─────────────────────────────────────────────────────────────

    /// <summary>
    /// A23: Admin realtime config (SignalR).
    /// Returns the hub URL so the Next.js console does not hardcode connection details.
    ///
    /// Connection pattern (signalr npm package):
    ///   const conn = new signalR.HubConnectionBuilder()
    ///     .withUrl(`${apiBase}/hubs/admin?access_token=${jwt}`)
    ///     .build();
    /// Market-group assignment is automatic on connect based on the "markets" JWT claim.
    /// </summary>
    [HttpGet("api/v1/admin/realtime/config")]
    public IActionResult GetRealtimeConfig()
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        return Ok(new
        {
            success = true,
            data = new
            {
                transport      = "signalr",
                hub_url        = "/hubs/admin",
                tls            = true,
                group_template = "admin-{market}"
            }
        });
    }

    /// <summary>
    /// A23: Kept for backward compatibility — returns the admin hub URL.
    /// With SignalR, market-scope enforcement is handled in AdminHub.OnConnectedAsync
    /// by reading the staff's "markets" JWT claim.
    /// </summary>
    [HttpPost("api/v1/admin/realtime/auth")]
    public IActionResult RealtimeAuth()
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        return Ok(new { success = true, data = new { hub_url = "/hubs/admin" } });
    }
}

// ── A18 Request shapes ────────────────────────────────────────────────────────

public record CreateZoneRequest(string Name, string PolygonGeoJson, string[]? Verticals,
    string? Status, decimal CenterLat, decimal CenterLng);
public record UpdateZoneRequest(string? Name, string? Status, string[]? Verticals,
    string? PolygonGeoJson);
public record UpdateContentRequest(string? Fr, string? En, bool Publish = true);
public record CreateBannerRequest(string ImageUrl, string? DeepLink, string? Placement,
    DateTime? StartsAt, DateTime? EndsAt, int Order = 0);
public record UpdateVersionConfigRequest(string Platform, string Latest, string Minimum,
    string? Gate, string? StoreUrl);
