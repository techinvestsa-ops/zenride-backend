using Izigo.Application.Features.Safety.Commands;
using Izigo.Application.Features.Safety.Dtos;
using Izigo.Application.Features.Safety.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1;

/// <summary>
/// Handles in-trip safety features: SOS alerts, trip sharing, incident
/// reporting, and safety check-ins.
/// </summary>
[Route("api/v1")]
public class SafetyController : BaseController
{
    // ── SOS ──────────────────────────────────────────────────────────────────

    /// <summary>Triggers an SOS alert for the current trip.</summary>
    [Authorize(Policy = "AppPolicy")]
    [HttpPost("safety/sos")]
    public async Task<IActionResult> TriggerSos([FromBody] TriggerSosRequest body)
        => Ok(await Mediator.Send(new TriggerSosCommand(CurrentUserId, body)));

    /// <summary>Marks an active SOS alert as resolved.</summary>
    [Authorize(Policy = "AppPolicy")]
    [HttpPost("safety/sos/{id}/resolve")]
    public async Task<IActionResult> ResolveSos(string id)
        => Ok(await Mediator.Send(new ResolveSosCommand(CurrentUserId, id)));

    // ── Trip Sharing ──────────────────────────────────────────────────────────

    /// <summary>Generates a shareable tracking link for an active trip.</summary>
    [Authorize(Policy = "RiderPolicy")]
    [HttpPost("trips/{id}/share")]
    public async Task<IActionResult> ShareTrip(string id)
        => Ok(await Mediator.Send(new ShareTripCommand(CurrentUserId, id)));

    /// <summary>Returns live trip status for a shared tracking token (public).</summary>
    [AllowAnonymous]
    [HttpGet("trips/shared/{token}")]
    public async Task<IActionResult> GetSharedTrip(string token)
        => Ok(await Mediator.Send(new GetSharedTripQuery(token)));

    // ── Reporting & Check-in ─────────────────────────────────────────────────

    /// <summary>Submits an incident or safety report for a trip.</summary>
    [Authorize(Policy = "AppPolicy")]
    [HttpPost("trips/{id}/report")]
    public async Task<IActionResult> ReportTrip(string id, [FromBody] ReportTripRequest body)
    {
        var role = HttpContext.Request.Headers["X-App"].FirstOrDefault() ?? "rider";
        await Mediator.Send(new ReportTripCommand(CurrentUserId, role, id, body));
        return NoContent();     // 204 — spec says no response body needed
    }

    /// <summary>Reports the other person on a co-ride booking.</summary>
    [Authorize(Policy = "AppPolicy")]
    [HttpPost("co-ride/bookings/{id}/report")]
    public async Task<IActionResult> ReportBooking(string id, [FromBody] ReportTripRequest body)
    {
        await Mediator.Send(new ReportBookingCommand(CurrentUserId, id, body));
        return NoContent();
    }

    /// <summary>The locked-account appeal thread. Suspended users can still call this.</summary>
    [Authorize(Policy = "AppPolicy")]
    [HttpGet("appeals")]
    public async Task<IActionResult> GetAppeal()
        => Ok(await Mediator.Send(new GetAppealQuery(CurrentUserId)));

    /// <summary>Sends an appeal message to support.</summary>
    [Authorize(Policy = "AppPolicy")]
    [HttpPost("appeals")]
    public async Task<IActionResult> PostAppeal([FromBody] AppealRequest body)
        => Ok(await Mediator.Send(new PostAppealCommand(CurrentUserId, body.Body)));

    /// <summary>Records a safety check-in from the user during an active trip.</summary>
    [Authorize(Policy = "AppPolicy")]
    [HttpPost("safety/checkin")]
    public async Task<IActionResult> SafetyCheckin([FromBody] SafetyCheckinRequest? body)
    {
        await Mediator.Send(new SafetyCheckinCommand(CurrentUserId, body));
        return NoContent();
    }
}
