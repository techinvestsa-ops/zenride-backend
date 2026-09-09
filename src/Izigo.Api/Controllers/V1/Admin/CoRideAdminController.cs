using Izigo.Application.Features.Admin.Growth.Commands;
using Izigo.Application.Features.Admin.Growth.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1.Admin;

[Route("api/v1/admin")]
[Authorize(Policy = "AdminPolicy")]
public class CoRideAdminController : AdminBaseController
{
    // ── Co-Ride Listings ──────────────────────────────────────────────────────

    [HttpGet("co-ride/listings")]
    public async Task<IActionResult> GetListings(
        [FromQuery] string? status, [FromQuery] string? zone,
        [FromQuery] string? driver_id,
        [FromQuery] int page = 1, [FromQuery] int per_page = 25,
        CancellationToken ct = default)
    {
        var check = CheckPermission("trips.view");
        if (check is not null) return check;

        return Ok(await Mediator.Send(
            new GetCoRideListingsQuery(status, zone, driver_id, page, per_page), ct));
    }

    [HttpGet("co-ride/listings/{id}")]
    public async Task<IActionResult> GetListing(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("trips.view");
        if (check is not null) return check;

        var dto = await Mediator.Send(new GetCoRideListingDetailQuery(id), ct);
        if (dto is null) return NotFound(new { success = false, error = new { code = "LISTING_NOT_FOUND" } });
        return Ok(new { success = true, data = dto });
    }

    [HttpPost("co-ride/listings/{id}/cancel")]
    public async Task<IActionResult> CancelListing(string id,
        [FromBody] CancelListingRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("trips.adjust");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new CancelCoRideListingCommand(
            id, req.Reason, req.RefundPassengers,
            StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return result.ErrorCode == "LISTING_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    [HttpPost("co-ride/bookings/{id}/refund")]
    public async Task<IActionResult> RefundBooking(string id,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct = default)
    {
        var check = CheckPermission("trips.refund");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest(new { success = false, error = new { code = "IDEMPOTENCY_KEY_REQUIRED" } });

        var result = await Mediator.Send(new RefundCoRideBookingCommand(
            id, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return result.ErrorCode == "BOOKING_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    [HttpGet("co-ride/requests")]
    public async Task<IActionResult> GetRequests(
        [FromQuery] int page = 1, [FromQuery] int per_page = 25,
        CancellationToken ct = default)
    {
        var check = CheckPermission("trips.view");
        if (check is not null) return check;

        return Ok(await Mediator.Send(new GetCoRideRequestsQuery(page, per_page), ct));
    }

    // ── Packages ──────────────────────────────────────────────────────────────

    [HttpGet("packages")]
    public async Task<IActionResult> GetPackages(
        [FromQuery] string? status, [FromQuery] bool? proof,
        [FromQuery] string? size, [FromQuery] bool? fragile,
        [FromQuery] string? courier_id,
        [FromQuery] int page = 1, [FromQuery] int per_page = 25,
        CancellationToken ct = default)
    {
        var check = CheckPermission("trips.view");
        if (check is not null) return check;

        return Ok(await Mediator.Send(new GetAdminPackagesQuery(
            Market, status, proof, size, fragile, courier_id, page, per_page), ct));
    }

    [HttpGet("packages/{id}")]
    public async Task<IActionResult> GetPackage(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("trips.view");
        if (check is not null) return check;

        var dto = await Mediator.Send(new GetAdminPackageDetailQuery(id), ct);
        if (dto is null) return NotFound(new { success = false, error = new { code = "PACKAGE_NOT_FOUND" } });
        return Ok(new { success = true, data = dto });
    }

    [HttpPost("packages/{id}/review-proof")]
    public async Task<IActionResult> ReviewProof(string id,
        [FromBody] ReviewProofRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("trips.adjust");
        if (check is not null) return check;

        if (req.Decision is not ("accept" or "reject"))
            return BadRequest(new { success = false, error = new { code = "INVALID_DECISION" } });

        var result = await Mediator.Send(new ReviewPackageProofCommand(
            id, req.Decision, req.Reason, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return result.ErrorCode == "PACKAGE_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    [HttpPost("packages/{id}/mark-lost")]
    public async Task<IActionResult> MarkLost(string id,
        [FromBody] MarkLostRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("trips.adjust");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new MarkPackageLostCommand(
            id, req.Reason, req.CompensationAmount, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return result.ErrorCode == "PACKAGE_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true, data = result.Data });
    }
}

// ── Request shapes ────────────────────────────────────────────────────────────

public record CancelListingRequest(string Reason, bool RefundPassengers = true);
public record ReviewProofRequest(string Decision, string? Reason);
public record MarkLostRequest(string Reason, long? CompensationAmount);
