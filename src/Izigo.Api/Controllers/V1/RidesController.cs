using Izigo.Application.Common.Models;
using Izigo.Application.Features.Rides.Commands;
using Izigo.Application.Features.Rides.Dtos;
using Izigo.Application.Features.Rides.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1;

/// <summary>
/// Core ride lifecycle controller: booking, tracking, in-trip mutations,
/// post-trip actions (rating, tipping, lost items), and history.
/// </summary>
[Route("api/v1/rides")]
[Authorize(Policy = "RiderPolicy")]
public class RidesController : BaseController
{
    /// <summary>Books a new ride from a confirmed quote.</summary>
    [HttpPost("")]
    public async Task<IActionResult> CreateRide(
        [FromBody] CreateRideRequest body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
        => Ok(await Mediator.Send(new CreateRideCommand(CurrentUserId, body, idempotencyKey)));

    /// <summary>Returns the rider's currently active (in-progress) ride.</summary>
    [HttpGet("active")]
    public async Task<IActionResult> GetActiveRide()
    {
        var result = await Mediator.Send(new GetActiveRideQuery(CurrentUserId));
        if (result == null) return NoContent();
        return Ok(result);
    }

    /// <summary>Returns all upcoming scheduled rides for the rider.</summary>
    [HttpGet("scheduled")]
    public async Task<IActionResult> GetScheduledRides()
        => Ok(await Mediator.Send(new GetScheduledRidesQuery(CurrentUserId)));

    /// <summary>Returns the list of platform-defined cancellation reasons.</summary>
    [Authorize(Policy = "AppPolicy")]
    [HttpGet("cancellation-reasons")]
    public async Task<IActionResult> GetCancellationReasons(
        [FromQuery] string audience = "rider",
        [FromQuery] string lang = "fr")
        => Ok(await Mediator.Send(new GetCancellationReasonsQuery(audience, lang)));

    /// <summary>Returns full details for a specific ride.</summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetRide(string id)
        => Ok(await Mediator.Send(new GetRideQuery(CurrentUserId, id)));

    /// <summary>Returns the real-time location of the driver for a given ride.</summary>
    [HttpGet("{id}/driver-location")]
    public async Task<IActionResult> GetDriverLocation(string id)
        => Ok(await Mediator.Send(new GetDriverLocationQuery(CurrentUserId, id)));

    /// <summary>Returns the post-trip receipt / fare breakdown for a completed ride.</summary>
    [HttpGet("{id}/receipt")]
    public async Task<IActionResult> GetReceipt(string id)
        => Ok(await Mediator.Send(new GetReceiptQuery(CurrentUserId, id)));

    /// <summary>Cancels an active or scheduled ride.</summary>
    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> CancelRide(string id, [FromBody] CancelRideRequest body)
        => Ok(await Mediator.Send(new CancelRideCommand(CurrentUserId, id, body.ReasonCode, body.Note)));

    /// <summary>Updates the drop-off destination of an ongoing ride.</summary>
    [HttpPatch("{id}/destination")]
    public async Task<IActionResult> UpdateDestination(string id, [FromBody] ChangeDestinationRequest body)
        => Ok(await Mediator.Send(new ChangeDestinationCommand(CurrentUserId, id, body)));

    /// <summary>Switches the payment method for an ongoing ride.</summary>
    [HttpPatch("{id}/payment-method")]
    public async Task<IActionResult> UpdatePaymentMethod(string id, [FromBody] ChangePaymentMethodRequest body)
    {
        await Mediator.Send(new ChangePaymentMethodCommand(CurrentUserId, id, body.Method));
        return NoContent();
    }

    /// <summary>Adds or replaces a note to the driver for a ride.</summary>
    [HttpPost("{id}/note")]
    public async Task<IActionResult> AddNote(string id, [FromBody] AddNoteRequest body)
    {
        await Mediator.Send(new AddNoteCommand(CurrentUserId, id, body.Text, body.AudioUrl));
        return NoContent();
    }

    /// <summary>Submits a rating and optional feedback for a completed ride.</summary>
    [HttpPost("{id}/rate")]
    public async Task<IActionResult> RateRide(string id, [FromBody] RateRideRequest body)
    {
        await Mediator.Send(new RateRideCommand(
            CurrentUserId, id, body.Stars, body.Tags, body.Comment, body.TipAmount));
        return NoContent();
    }

    /// <summary>Adds a tip to the driver after the ride is complete.</summary>
    [HttpPost("{id}/tip")]
    public async Task<IActionResult> TipDriver(string id, [FromBody] TipRideRequest body)
    {
        await Mediator.Send(new TipRideCommand(CurrentUserId, id, body.Amount));
        return NoContent();
    }

    /// <summary>Reports a lost item for a completed ride.</summary>
    [HttpPost("{id}/lost-item")]
    public async Task<IActionResult> ReportLostItem(string id, [FromBody] ReportLostItemRequest body)
    {
        await Mediator.Send(new ReportLostItemCommand(CurrentUserId, id, body.Description));
        return NoContent();
    }

    /// <summary>Returns a paginated list of the rider's past rides.</summary>
    [HttpGet("")]
    public async Task<IActionResult> GetRides(
        [FromQuery] string? status,
        [FromQuery] string? vertical,
        [FromQuery] string? category,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int per_page = 20)
    {
        var (items, total) = await Mediator.Send(
            new GetRidesQuery(CurrentUserId, status, vertical, category, from, to, page, per_page));
        return Ok(items, PagedMeta.From(page, per_page, total));
    }

    /// <summary>Cancels and removes a specific scheduled ride.</summary>
    [HttpDelete("scheduled/{id}")]
    public async Task<IActionResult> DeleteScheduledRide(string id)
    {
        await Mediator.Send(new CancelScheduledRideCommand(CurrentUserId, id));
        return NoContent();
    }
}
