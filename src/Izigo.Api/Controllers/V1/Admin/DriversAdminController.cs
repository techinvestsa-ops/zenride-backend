using Izigo.Application.Features.Admin.Drivers.Commands;
using Izigo.Application.Features.Admin.Drivers.Queries;
using Izigo.Application.Features.Admin.Ops.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1.Admin;

/// <summary>
/// Admin driver management (A08) and driver moderation (A20):
/// roster, profile, jobs, ratings, location history (A24), suspend, verticals,
/// vehicle, performance reset, edit details, force password reset, revoke sessions, block/unblock.
/// </summary>
[Route("api/v1/admin/drivers")]
[Authorize(Policy = "AdminPolicy")]
public class DriversAdminController : AdminBaseController
{
    // ── A08 Reads ─────────────────────────────────────────────────────────────

    /// <summary>Returns the driver roster. Filters: kyc_status, online, vertical, vehicle_type, zone, suspended, has_cash_owed, docs_expiring_within_days.</summary>
    [HttpGet("")]
    public async Task<IActionResult> GetDrivers(
        [FromQuery] string? kyc_status, [FromQuery] bool? online, [FromQuery] string? vertical,
        [FromQuery] string? vehicle_type, [FromQuery] string? zone, [FromQuery] bool? suspended,
        [FromQuery] bool? has_cash_owed, [FromQuery] int? docs_expiring_within_days,
        [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int per_page = 25,
        CancellationToken ct = default)
    {
        var check = CheckPermission("drivers.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetAdminDriversQuery(Market, kyc_status, online, vertical,
            vehicle_type, zone, suspended, has_cash_owed, docs_expiring_within_days, q, page, per_page), ct));
    }

    /// <summary>Returns the full driver profile: identity, vehicle, verticals, performance, money block, documents with expiry.</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetDriver(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("drivers.view");
        if (check is not null) return check;

        var dto = await Mediator.Send(new GetAdminDriverDetailQuery(id), ct);
        if (dto is null)
            return NotFound(new { success = false, error = new { code = "DRIVER_NOT_FOUND" } });
        return Ok(new { success = true, data = dto });
    }

    /// <summary>Returns the driver's job history with per-job earnings and commission.</summary>
    [HttpGet("{id}/jobs")]
    public async Task<IActionResult> GetDriverJobs(string id,
        [FromQuery] int page = 1, [FromQuery] int per_page = 25, CancellationToken ct = default)
    {
        var check = CheckPermission("drivers.view");
        if (check is not null) return check;

        var result = await Mediator.Send(new GetDriverJobsQuery(id, Market, page, per_page), ct);
        if (result is null)
            return NotFound(new { success = false, error = new { code = "DRIVER_NOT_FOUND" } });
        return Ok(result);
    }

    /// <summary>Returns the GPS location history for a specific driver. Each access is audited (A24).</summary>
    [HttpGet("{id}/location-history")]
    public async Task<IActionResult> GetLocationHistory(string id,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, CancellationToken ct = default)
    {
        var check = CheckPermission("drivers.view");
        if (check is not null) return check;

        var result = await Mediator.Send(new GetLocationHistoryQuery(
            id, from, to, StaffId, CurrentStaff.Email ?? StaffId, Market), ct);

        if (result is null)
            return NotFound(new { success = false, error = new { code = "DRIVER_NOT_FOUND" } });
        return Ok(result);
    }

    /// <summary>Returns the ratings received by this driver: stars, tags, comments, trip codes.</summary>
    [HttpGet("{id}/ratings")]
    public async Task<IActionResult> GetDriverRatings(string id,
        [FromQuery] int page = 1, [FromQuery] int per_page = 25, CancellationToken ct = default)
    {
        var check = CheckPermission("drivers.view");
        if (check is not null) return check;

        var result = await Mediator.Send(new GetDriverRatingsQuery(id, page, per_page), ct);
        if (result is null)
            return NotFound(new { success = false, error = new { code = "DRIVER_NOT_FOUND" } });
        return Ok(result);
    }

    /// <summary>Exports driver data as CSV with the same filters. Returns 202 + job_id.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> ExportDrivers(CancellationToken ct = default)
    {
        var check = CheckPermission("drivers.view");
        if (check is not null) return check;

        var result = await Mediator.Send(new ExportDriversQuery(StaffId, Market), ct);
        return Accepted(new { success = true, data = new { job_id = result.JobId, status_url = result.StatusUrl } });
    }

    // ── A08 Writes ────────────────────────────────────────────────────────────

    /// <summary>Suspends a driver, forces them offline, and cancels any active job with driver-fault handling.</summary>
    [HttpPost("{id}/suspend")]
    public async Task<IActionResult> SuspendDriver(string id,
        [FromBody] SuspendDriverRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("drivers.moderate");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new SuspendDriverCommand(
            id, req.Reason, req.Until, StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true })
            : NotFound(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Updates the service verticals a driver is permitted to serve, constrained by vehicle type.</summary>
    [HttpPatch("{id}/verticals")]
    public async Task<IActionResult> UpdateVerticals(string id,
        [FromBody] UpdateVerticalsRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("drivers.edit");
        if (check is not null) return check;

        var result = await Mediator.Send(new UpdateDriverVerticalsCommand(
            id, req.Verticals, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return result.ErrorCode == "DRIVER_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    /// <summary>Corrects vehicle details. A plate or type change re-triggers vehicle review.</summary>
    [HttpPatch("{id}/vehicle")]
    public async Task<IActionResult> UpdateVehicle(string id,
        [FromBody] UpdateVehicleRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("drivers.edit");
        if (check is not null) return check;

        var result = await Mediator.Send(new UpdateDriverVehicleCommand(
            id, req.Plate, req.VehicleType, StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true })
            : NotFound(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Resets a specific driver performance metric. Rare and heavily audited.</summary>
    [HttpPost("{id}/reset-performance")]
    public async Task<IActionResult> ResetPerformance(string id,
        [FromBody] ResetPerformanceRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("drivers.moderate");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new ResetDriverPerformanceCommand(
            id, req.Metric, req.Reason, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return result.ErrorCode == "DRIVER_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : BadRequest(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    // ── A20 Driver Moderation ─────────────────────────────────────────────────

    /// <summary>Edits driver identity details. Name change re-opens the identity KYC step.</summary>
    [HttpPatch("{id}")]
    public async Task<IActionResult> UpdateDriver(string id,
        [FromBody] UpdateDriverRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("drivers.edit");
        if (check is not null) return check;

        var result = await Mediator.Send(new UpdateDriverCommand(
            id, req.FirstName, req.LastName, req.Email, req.Phone,
            StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true })
            : NotFound(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Sends a password reset link to the driver's phone. Admins never set a driver's password directly.</summary>
    [HttpPost("{id}/password/reset-link")]
    public async Task<IActionResult> SendPasswordResetLink(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("drivers.moderate");
        if (check is not null) return check;

        var result = await Mediator.Send(
            new SendDriverPasswordResetLinkCommand(id, StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true })
            : NotFound(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Revokes all driver sessions everywhere and forces them offline.</summary>
    [HttpPost("{id}/sessions/revoke")]
    public async Task<IActionResult> RevokeSessions(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("drivers.moderate");
        if (check is not null) return check;

        var result = await Mediator.Send(
            new RevokeDriverSessionsCommand(id, StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true })
            : NotFound(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Blocks a driver: forces offline, cancels active job, reports cash_owed and wallet_balance so nothing is stranded.</summary>
    [HttpPost("{id}/block")]
    public async Task<IActionResult> BlockDriver(string id,
        [FromBody] BlockDriverRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("drivers.moderate");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new BlockDriverCommand(
            id, req.Reason, req.SettleCash, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return NotFound(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true, data = result.Data });
    }

    /// <summary>Unblocks a driver and re-checks document expiry before allowing them online again.</summary>
    [HttpPost("{id}/unblock")]
    public async Task<IActionResult> UnblockDriver(string id,
        [FromBody] ReasonOnlyRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("drivers.moderate");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(
            new UnblockDriverCommand(id, req.Reason, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return NotFound(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true, data = result.Data });
    }
}

// ── Request shapes ────────────────────────────────────────────────────────────

public record SuspendDriverRequest(string Reason, DateTime? Until);
public record UpdateVerticalsRequest(List<string> Verticals);
public record UpdateVehicleRequest(string? Plate, string? VehicleType);
public record ResetPerformanceRequest(string Metric, string Reason);
public record UpdateDriverRequest(string? FirstName, string? LastName, string? Email, string? Phone);
public record BlockDriverRequest(string Reason, bool SettleCash = false);
