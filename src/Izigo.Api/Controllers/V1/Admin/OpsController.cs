using Izigo.Application.Features.Admin.Ops.Commands;
using Izigo.Application.Features.Admin.Ops.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1.Admin;

/// <summary>
/// Provides real-time operational tooling: live job monitoring, online driver
/// visibility, demand snapshots, manual interventions, and geo heatmaps (A03 + A24).
/// </summary>
[Route("api/v1/admin/ops")]
[Authorize(Policy = "AdminPolicy")]
public class OpsController : AdminBaseController
{
    // ── A03 Live Ops ──────────────────────────────────────────────────────────

    /// <summary>Returns a live snapshot of all active jobs with their current state, driver, rider, fare, and ETA summary.</summary>
    [HttpGet("live")]
    public async Task<IActionResult> GetLive(CancellationToken ct = default)
    {
        var check = CheckPermission("ops.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetLiveOpsQuery(Market), ct));
    }

    /// <summary>Returns online drivers with live positions, zone, state, and vertical. Filterable by zone, vertical, state.</summary>
    [HttpGet("drivers-online")]
    public async Task<IActionResult> GetDriversOnline(
        [FromQuery] string? zone, [FromQuery] string? vertical, [FromQuery] string? state,
        CancellationToken ct = default)
    {
        var check = CheckPermission("ops.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetDriversOnlineQuery(Market, zone, vertical, state), ct));
    }

    /// <summary>Returns current demand distribution per zone with surge multiplier and unmatched count.</summary>
    [HttpGet("demand")]
    public async Task<IActionResult> GetDemand(CancellationToken ct = default)
    {
        var check = CheckPermission("ops.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetDemandQuery(Market), ct));
    }

    /// <summary>Manually reassigns a job to a different driver, or re-broadcasts if driver_id is omitted. Notifies both drivers and the rider.</summary>
    [HttpPost("jobs/{id}/reassign")]
    public async Task<IActionResult> ReassignJob(string id,
        [FromBody] ReassignJobRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("ops.write");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new ReassignJobCommand(
            id, req.DriverId, req.Reason, StaffId, CurrentStaff.Email ?? StaffId, Market), ct);

        if (!result.Success)
            return result.ErrorCode == "TRIP_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    /// <summary>Manually cancels an active job from the operations dashboard, setting cancelled_by_admin.</summary>
    [HttpPost("jobs/{id}/cancel")]
    public async Task<IActionResult> CancelJob(string id,
        [FromBody] CancelJobRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("ops.write");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new CancelJobCommand(
            id, req.Reason, req.WaiveFee, req.CompensateDriver,
            StaffId, CurrentStaff.Email ?? StaffId, Market), ct);

        if (!result.Success)
            return result.ErrorCode == "TRIP_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    /// <summary>Forces a driver offline immediately and pushes driver.forced_offline so the app toggle updates.</summary>
    [HttpPost("drivers/{id}/force-offline")]
    public async Task<IActionResult> ForceDriverOffline(string id,
        [FromBody] ForceOfflineRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("ops.write");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new ForceDriverOfflineCommand(
            id, req.Reason, StaffId, CurrentStaff.Email ?? StaffId, Market), ct);

        if (!result.Success)
            return NotFound(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    /// <summary>Sends a push notification nudging online drivers towards a specific zone.</summary>
    [HttpPost("broadcast-zone")]
    public async Task<IActionResult> BroadcastZone(
        [FromBody] BroadcastZoneRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("ops.write");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.ZoneId) || string.IsNullOrWhiteSpace(req.Message))
            return BadRequest(new { success = false, error = new { code = "ZONE_ID_AND_MESSAGE_REQUIRED" } });

        var result = await Mediator.Send(new BroadcastZoneCommand(
            req.ZoneId, req.Message, req.Incentive,
            StaffId, CurrentStaff.Email ?? StaffId, Market), ct);

        if (!result.Success)
            return NotFound(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    // ── A24 Map & Geo (served from /ops prefix) ───────────────────────────────

    /// <summary>Returns real-time tracking for a specific job: driver position, route polyline, pickup/dropoff pins, ETA.</summary>
    [HttpGet("jobs/{id}/track")]
    public async Task<IActionResult> TrackJob(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("ops.view");
        if (check is not null) return check;

        var result = await Mediator.Send(new GetJobTrackQuery(id), ct);
        if (result is null)
            return NotFound(new { success = false, error = new { code = "TRIP_NOT_FOUND" } });

        return Ok(result);
    }

    /// <summary>Returns a demand density heatmap. ?metric=requests|unmatched|surge&amp;bucket=hex → cells with weight.</summary>
    [HttpGet("heatmap")]
    public async Task<IActionResult> GetHeatmap(
        [FromQuery] string metric = "requests", CancellationToken ct = default)
    {
        var check = CheckPermission("ops.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetHeatmapQuery(Market, metric), ct));
    }
}

// ── Request shapes ────────────────────────────────────────────────────────────

public record ReassignJobRequest(string? DriverId, string Reason);
public record CancelJobRequest(string Reason, bool WaiveFee = false, bool CompensateDriver = false);
public record ForceOfflineRequest(string Reason);
public record BroadcastZoneRequest(string ZoneId, string Message, string? Incentive);
