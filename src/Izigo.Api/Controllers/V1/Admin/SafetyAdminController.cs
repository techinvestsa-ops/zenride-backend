using Izigo.Application.Features.Admin.Safety.Commands;
using Izigo.Application.Features.Admin.Safety.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1.Admin;

[Route("api/v1/admin/safety")]
[Authorize(Policy = "AdminPolicy")]
public class SafetyAdminController : AdminBaseController
{
    /// <summary>
    /// Incident log. Filters: status, type, source, zone, from, to.
    /// Row includes response_seconds (time from raised to acknowledged).
    /// </summary>
    [HttpGet("incidents")]
    public async Task<IActionResult> GetIncidents(
        [FromQuery] string? status, [FromQuery] string? type,
        [FromQuery] string? source, [FromQuery] string? zone,
        [FromQuery] DateTime? from, [FromQuery] DateTime? to,
        [FromQuery] int page = 1, [FromQuery] int per_page = 25,
        CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        return Ok(await Mediator.Send(
            new GetSosIncidentsQuery(Market, status, type, source, from, to, page, per_page), ct));
    }

    /// <summary>
    /// Incident detail: live location trail, linked trip, emergency contacts, full response log.
    /// </summary>
    [HttpGet("incidents/{id}")]
    public async Task<IActionResult> GetIncident(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        var dto = await Mediator.Send(new GetSosIncidentDetailQuery(id), ct);
        if (dto is null) return NotFound(new { success = false, error = new { code = "INCIDENT_NOT_FOUND" } });
        return Ok(new { success = true, data = dto });
    }

    /// <summary>
    /// Claim the incident — stops the pager escalation and records who owns it.
    /// This is the call that stops the clock.
    /// </summary>
    [HttpPost("incidents/{id}/acknowledge")]
    public async Task<IActionResult> AcknowledgeIncident(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        var result = await Mediator.Send(new AcknowledgeSosIncidentCommand(
            id, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return result.ErrorCode == "INCIDENT_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : Conflict(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    /// <summary>
    /// Close out an incident. outcome = resolved | false_alarm | escalated.
    /// </summary>
    [HttpPost("incidents/{id}/resolve")]
    public async Task<IActionResult> ResolveIncident(string id,
        [FromBody] ResolveIncidentRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Outcome))
            return BadRequest(new { success = false, error = new { code = "OUTCOME_REQUIRED" } });

        var result = await Mediator.Send(new ResolveSosIncidentCommand(
            id, req.Outcome, req.Notes, StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return result.ErrorCode is "INCIDENT_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    /// <summary>
    /// Hand to authorities. authority and reference are required for the handoff record.
    /// </summary>
    [HttpPost("incidents/{id}/escalate")]
    public async Task<IActionResult> EscalateIncident(string id,
        [FromBody] EscalateIncidentRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Authority))
            return BadRequest(new { success = false, error = new { code = "AUTHORITY_REQUIRED" } });

        var result = await Mediator.Send(new EscalateSosIncidentCommand(
            id, req.Authority, req.Reference, req.Notes,
            StaffId, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return result.ErrorCode is "INCIDENT_NOT_FOUND"
                ? NotFound(new { success = false, error = new { code = result.ErrorCode } })
                : UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    /// <summary>
    /// Safety metrics: median + p95 response seconds, incidents per 1000 trips, by type, by zone.
    /// </summary>
    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics(CancellationToken ct = default)
    {
        var check = CheckPermission("dashboard.view");
        if (check is not null) return check;

        return Ok(await Mediator.Send(new GetSafetyMetricsQuery(Market), ct));
    }
}

// ── Request shapes ────────────────────────────────────────────────────────────

public record ResolveIncidentRequest(string Outcome, string? Notes);
public record EscalateIncidentRequest(string Authority, string? Reference, string? Notes);
