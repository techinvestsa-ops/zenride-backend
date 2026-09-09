using Izigo.Application.Features.CoRide.Commands;
using Izigo.Application.Features.CoRide.Dtos;
using Izigo.Application.Features.CoRide.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1;

/// <summary>
/// Manages the co-ride (shared ride) feature: browsing available listings,
/// submitting join requests, managing bookings, and post-trip rating.
/// </summary>
[Route("api/v1/co-ride")]
[Authorize(Policy = "RiderPolicy")]
public class CoRideController : BaseController
{
    // ── Listings ─────────────────────────────────────────────────────────────

    /// <summary>Returns a list of available co-ride listings matching the search criteria.</summary>
    [HttpGet("listings")]
    public async Task<IActionResult> GetListings([FromQuery] SearchListingsRequest req)
        => Ok(await Mediator.Send(new SearchListingsQuery(CurrentUserId, req)));

    /// <summary>Returns details of a specific co-ride listing.</summary>
    [HttpGet("listings/{id}")]
    public async Task<IActionResult> GetListing(string id)
        => Ok(await Mediator.Send(new GetListingQuery(CurrentUserId, id)));

    // ── Requests ─────────────────────────────────────────────────────────────

    /// <summary>Submits a join request to a co-ride listing.</summary>
    [HttpPost("requests")]
    public async Task<IActionResult> CreateRequest([FromBody] CreateMatchRequest body)
        => Ok(await Mediator.Send(new CreateMatchRequestCommand(CurrentUserId, body)));

    /// <summary>Returns all co-ride join requests made by the authenticated rider.</summary>
    [HttpGet("requests")]
    public async Task<IActionResult> GetRequests()
        => Ok(await Mediator.Send(new GetMyRequestsQuery(CurrentUserId)));

    /// <summary>Withdraws a pending co-ride join request.</summary>
    [HttpDelete("requests/{id}")]
    public async Task<IActionResult> DeleteRequest(string id)
    {
        await Mediator.Send(new WithdrawRequestCommand(CurrentUserId, id));
        return NoContent();
    }

    // ── Bookings ─────────────────────────────────────────────────────────────

    /// <summary>Creates a co-ride booking (seat reservation) on a listing.</summary>
    [HttpPost("bookings")]
    public async Task<IActionResult> CreateBooking(
        [FromBody] BookSeatsRequest body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
        => Ok(await Mediator.Send(new BookSeatsCommand(CurrentUserId, body, idempotencyKey)));

    /// <summary>Returns all co-ride bookings for the authenticated rider.</summary>
    [HttpGet("bookings")]
    public async Task<IActionResult> GetBookings([FromQuery] string state = "active")
        => Ok(await Mediator.Send(new GetMyBookingsQuery(CurrentUserId, state)));

    /// <summary>Returns details of a specific co-ride booking.</summary>
    [HttpGet("bookings/{id}")]
    public async Task<IActionResult> GetBooking(string id)
        => Ok(await Mediator.Send(new GetBookingQuery(CurrentUserId, id)));

    /// <summary>Cancels an upcoming co-ride booking.</summary>
    [HttpPost("bookings/{id}/cancel")]
    public async Task<IActionResult> CancelBooking(string id, [FromBody] CancelCoRideRequest? body)
    {
        await Mediator.Send(new CancelBookingCommand(CurrentUserId, id, body?.Reason));
        return NoContent();
    }

    /// <summary>Submits a rating for a completed co-ride booking.</summary>
    [HttpPost("bookings/{id}/rate")]
    public async Task<IActionResult> RateBooking(string id, [FromBody] RateCoRideRequest body)
    {
        await Mediator.Send(new RateCoRideCommand(CurrentUserId, id, body));
        return NoContent();
    }
}
