using Izigo.Application.Features.Admin.Trips.Commands;
using Izigo.Application.Features.Admin.Trips.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1.Admin;

/// <summary>
/// Provides admin-level trip management: browsing all verticals, full detail,
/// GPS route, refund, fare adjustment, dispute reopen, and CSV export (A04).
/// </summary>
[Route("api/v1/admin/trips")]
[Authorize(Policy = "AdminPolicy")]
public class TripsAdminController : AdminBaseController
{
    /// <summary>
    /// Returns a filterable, paginated list of all platform trips across all verticals.
    /// Filters: vertical, status, payment_method, pay_status, zone, driver_id, rider_id, from, to, min_fare.
    /// </summary>
    [HttpGet("")]
    public async Task<IActionResult> GetTrips(
        [FromQuery] string? vertical,
        [FromQuery] string? status,
        [FromQuery] string? payment_method,
        [FromQuery] string? pay_status,
        [FromQuery] string? zone,
        [FromQuery] string? driver_id,
        [FromQuery] string? rider_id,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] long? min_fare,
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int per_page = 25,
        CancellationToken ct = default)
    {
        var check = CheckPermission("trips.view");
        if (check is not null) return check;

        return Ok(await Mediator.Send(new GetAdminTripsQuery(
            Market, vertical, status, payment_method, pay_status,
            zone, driver_id, rider_id, from, to, min_fare, q, page, per_page), ct));
    }

    /// <summary>Returns full trip detail: fare breakdown, settlement, payment, ratings, state timeline, and allowed actions.</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetTrip(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("trips.view");
        if (check is not null) return check;

        var dto = await Mediator.Send(new GetAdminTripDetailQuery(id), ct);
        if (dto is null)
            return NotFound(new { success = false, error = new { code = "TRIP_NOT_FOUND" } });

        return Ok(new { success = true, data = dto });
    }

    /// <summary>Returns the encoded GPS polyline of the trip's actual route, for dispute investigation.</summary>
    [HttpGet("{id}/route")]
    public async Task<IActionResult> GetTripRoute(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("trips.view");
        if (check is not null) return check;

        var dto = await Mediator.Send(new GetTripRouteQuery(id), ct);
        if (dto is null)
            return NotFound(new { success = false, error = new { code = "TRIP_NOT_FOUND" } });

        return Ok(new { success = true, data = dto });
    }

    /// <summary>Issues a full or partial refund for a completed trip. Requires trips.refund permission and an Idempotency-Key.</summary>
    [HttpPost("{id}/refund")]
    public async Task<IActionResult> RefundTrip(string id,
        [FromBody] RefundTripRequest req,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct = default)
    {
        var check = CheckPermission("trips.refund");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest(new { success = false, error = new { code = "IDEMPOTENCY_KEY_REQUIRED" } });

        var destination = req.Destination?.ToLower();
        if (destination is not ("original" or "wallet"))
            return BadRequest(new { success = false, error = new { code = "INVALID_DESTINATION" } });

        var result = await Mediator.Send(new RefundTripCommand(
            id, req.Amount, req.Reason, destination, req.RecoverFromDriver,
            idempotencyKey, StaffId, CurrentStaff.Email ?? StaffId, Market), ct);

        if (!result.Success)
            return result.ErrorCode == "TRIP_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    /// <summary>Corrects the final fare for a completed trip, recomputing commission and driver earnings across both ledger legs.</summary>
    [HttpPost("{id}/adjust-fare")]
    public async Task<IActionResult> AdjustFare(string id,
        [FromBody] AdjustFareRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("trips.adjust");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        if (req.NewTotal <= 0)
            return BadRequest(new { success = false, error = new { code = "INVALID_FARE" } });

        var result = await Mediator.Send(new AdjustFareCommand(
            id, req.NewTotal, req.Reason, StaffId, CurrentStaff.Email ?? StaffId, Market), ct);

        if (!result.Success)
            return result.ErrorCode == "TRIP_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    /// <summary>Reopens a completed or cancelled trip for dispute investigation; moves it to disputed state and creates a support ticket.</summary>
    [HttpPost("{id}/reopen")]
    public async Task<IActionResult> ReopenTrip(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("trips.adjust");
        if (check is not null) return check;

        var result = await Mediator.Send(
            new ReopenTripCommand(id, StaffId, CurrentStaff.Email ?? StaffId, Market), ct);

        if (!result.Success)
            return result.ErrorCode == "TRIP_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : Conflict(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    /// <summary>Exports trip data with the same filters as the list; returns 202 with job_id for large ranges.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> ExportTrips(
        [FromQuery] string? vertical,
        [FromQuery] string? status,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        var check = CheckPermission("trips.view");
        if (check is not null) return check;

        var result = await Mediator.Send(
            new ExportTripsQuery(StaffId, Market, vertical, status, from, to), ct);

        return Accepted(new { success = true, data = new { job_id = result.JobId, status_url = result.StatusUrl } });
    }
}

// ── Request shapes ────────────────────────────────────────────────────────────

public record RefundTripRequest(long? Amount, string Reason, string? Destination, bool RecoverFromDriver = false);
public record AdjustFareRequest(long NewTotal, string Reason);
