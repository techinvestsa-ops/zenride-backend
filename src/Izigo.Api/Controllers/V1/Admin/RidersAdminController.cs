using Izigo.Application.Features.Admin.Riders.Commands;
using Izigo.Application.Features.Admin.Riders.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1.Admin;

/// <summary>
/// Admin rider management (A07) and rider moderation (A20):
/// search, profile, trip history, devices, suspend/flag/block/unblock/delete,
/// edit details, force password reset, revoke sessions.
/// </summary>
[Route("api/v1/admin/riders")]
[Authorize(Policy = "AdminPolicy")]
public class RidersAdminController : AdminBaseController
{
    // ── A07 Reads ─────────────────────────────────────────────────────────────

    /// <summary>Returns a searchable, paginated rider list. Filters: status, zone, min_trips, has_wallet_balance.</summary>
    [HttpGet("")]
    public async Task<IActionResult> GetRiders(
        [FromQuery] string? status, [FromQuery] string? zone,
        [FromQuery] int? min_trips, [FromQuery] bool? has_wallet_balance,
        [FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int per_page = 25,
        CancellationToken ct = default)
    {
        var check = CheckPermission("riders.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(
            new GetAdminRidersQuery(Market, status, zone, min_trips, has_wallet_balance, q, page, per_page), ct));
    }

    /// <summary>Returns the full rider profile: counters, recent trips, wallet ledger, devices, flags, referral tree.</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetRider(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("riders.view");
        if (check is not null) return check;

        var dto = await Mediator.Send(new GetAdminRiderDetailQuery(id), ct);
        if (dto is null)
            return NotFound(new { success = false, error = new { code = "RIDER_NOT_FOUND" } });
        return Ok(new { success = true, data = dto });
    }

    /// <summary>Returns the rider's trip history, same shape as /admin/trips.</summary>
    [HttpGet("{id}/trips")]
    public async Task<IActionResult> GetRiderTrips(string id,
        [FromQuery] int page = 1, [FromQuery] int per_page = 25, CancellationToken ct = default)
    {
        var check = CheckPermission("riders.view");
        if (check is not null) return check;
        return Ok(await Mediator.Send(new GetRiderTripsQuery(id, Market, page, per_page), ct));
    }

    /// <summary>Returns the rider's registered devices with push token state and duplicate-device detection.</summary>
    [HttpGet("{id}/devices")]
    public async Task<IActionResult> GetRiderDevices(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("riders.view");
        if (check is not null) return check;

        var result = await Mediator.Send(new GetRiderDevicesQuery(id), ct);
        if (result is null)
            return NotFound(new { success = false, error = new { code = "RIDER_NOT_FOUND" } });
        return Ok(result);
    }

    // ── A07 Writes ────────────────────────────────────────────────────────────

    /// <summary>Suspends or reinstates a rider. action=suspend|reinstate. Reason is shown in the app on next launch.</summary>
    [HttpPost("{id}/suspend")]
    public async Task<IActionResult> SuspendRider(string id,
        [FromBody] SuspendRiderRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("riders.moderate");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        if (req.Action is not ("suspend" or "reinstate"))
            return BadRequest(new { success = false, error = new { code = "INVALID_ACTION" } });

        var result = await Mediator.Send(new SuspendRiderCommand(
            id, req.Action, req.Reason, req.Until, StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true })
            : NotFound(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Flags a rider for review. Does not block the account but surfaces on every screen they touch.</summary>
    [HttpPost("{id}/flag")]
    public async Task<IActionResult> FlagRider(string id,
        [FromBody] FlagRiderRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("riders.moderate");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var severity = req.Severity?.ToLower() ?? "medium";
        if (severity is not ("low" or "medium" or "high"))
            return BadRequest(new { success = false, error = new { code = "INVALID_SEVERITY" } });

        var result = await Mediator.Send(
            new FlagRiderCommand(id, req.Reason, severity, StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true })
            : NotFound(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Revokes all active sessions for a rider (reported account takeover).</summary>
    [HttpPost("{id}/logout-all")]
    public async Task<IActionResult> LogoutAllSessions(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("riders.moderate");
        if (check is not null) return check;

        var result = await Mediator.Send(
            new LogoutRiderAllCommand(id, StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true })
            : NotFound(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Anonymises a rider account. 409 if wallet balance or open trip exists. Trips and ledger survive.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteRider(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("riders.moderate");
        if (check is not null) return check;

        var result = await Mediator.Send(
            new DeleteRiderCommand(id, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return result.ErrorCode == "RIDER_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : Conflict(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    // ── A20 Rider Moderation ──────────────────────────────────────────────────

    /// <summary>Edits rider profile details. Phone change requires re-verification before it becomes the sign-in credential.</summary>
    [HttpPatch("{id}")]
    public async Task<IActionResult> UpdateRider(string id,
        [FromBody] UpdateRiderRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("riders.edit");
        if (check is not null) return check;

        var result = await Mediator.Send(new UpdateRiderCommand(
            id, req.FirstName, req.LastName, req.Email, req.Phone, req.Language,
            StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true })
            : NotFound(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Sends a password reset link to the rider's registered phone. Admins never set a customer's password directly.</summary>
    [HttpPost("{id}/password/reset-link")]
    public async Task<IActionResult> SendPasswordResetLink(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("riders.moderate");
        if (check is not null) return check;

        var result = await Mediator.Send(
            new SendRiderPasswordResetLinkCommand(id, StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true })
            : NotFound(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Revokes all active sessions for a rider everywhere.</summary>
    [HttpPost("{id}/sessions/revoke")]
    public async Task<IActionResult> RevokeSessions(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("riders.moderate");
        if (check is not null) return check;

        var result = await Mediator.Send(
            new RevokeRiderSessionsCommand(id, StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true })
            : NotFound(new { success = false, error = new { code = result.ErrorCode } });
    }

    /// <summary>Blocks a rider. Returns whether wallet balance is stranded so the operator can refund it first.</summary>
    [HttpPost("{id}/block")]
    public async Task<IActionResult> BlockRider(string id,
        [FromBody] BlockRiderRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("riders.moderate");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new BlockRiderCommand(
            id, req.Reason, req.CancelActiveTrip, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return NotFound(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true, data = result.Data });
    }

    /// <summary>Unblocks a previously blocked rider.</summary>
    [HttpPost("{id}/unblock")]
    public async Task<IActionResult> UnblockRider(string id,
        [FromBody] ReasonOnlyRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("riders.moderate");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(
            new UnblockRiderCommand(id, req.Reason, StaffId, CurrentStaff.Email ?? StaffId), ct);

        return result.Success
            ? Ok(new { success = true })
            : NotFound(new { success = false, error = new { code = result.ErrorCode } });
    }
}

// ── Request shapes ────────────────────────────────────────────────────────────

public record SuspendRiderRequest(string Action, string Reason, DateTime? Until);
public record FlagRiderRequest(string Reason, string? Severity);
public record UpdateRiderRequest(string? FirstName, string? LastName, string? Email,
    string? Phone, string? Language);
public record BlockRiderRequest(string Reason, bool CancelActiveTrip = false);
public record ReasonOnlyRequest(string Reason);
