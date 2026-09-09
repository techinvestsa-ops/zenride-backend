using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Safety.Commands;

public record SafetyCommandResult(bool Success, string? ErrorCode);

// ── POST /admin/safety/incidents/{id}/acknowledge ─────────────────────────────
// Stops the pager escalation; records who owns it; starts the response clock.

public record AcknowledgeSosIncidentCommand(string IncidentId, string StaffId, string StaffName)
    : IRequest<SafetyCommandResult>;

public class AcknowledgeSosIncidentHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<AcknowledgeSosIncidentCommand, SafetyCommandResult>
{
    public async Task<SafetyCommandResult> Handle(AcknowledgeSosIncidentCommand cmd, CancellationToken ct)
    {
        var incident = await db.SosIncidents.FirstOrDefaultAsync(s => s.Id == cmd.IncidentId, ct);
        if (incident is null) return new(false, "INCIDENT_NOT_FOUND");
        if (incident.AcknowledgedAt.HasValue) return new(false, "ALREADY_ACKNOWLEDGED");

        var before = new { incident.AcknowledgedByStaffId, incident.AcknowledgedAt };
        incident.AcknowledgedByStaffId = cmd.StaffId;
        incident.AcknowledgedAt        = DateTime.UtcNow;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.SosAcknowledge,
            "SosIncident", cmd.IncidentId,
            before: before, after: new { AcknowledgedBy = cmd.StaffId }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/safety/incidents/{id}/resolve ─────────────────────────────────
// outcome = resolved | false_alarm | escalated, optional notes.

public record ResolveSosIncidentCommand(string IncidentId, string Outcome, string? Notes,
    string StaffId, string StaffName) : IRequest<SafetyCommandResult>;

public class ResolveSosIncidentHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<ResolveSosIncidentCommand, SafetyCommandResult>
{
    private static readonly HashSet<string> ValidOutcomes =
        new(StringComparer.OrdinalIgnoreCase) { "resolved", "false_alarm", "escalated" };

    public async Task<SafetyCommandResult> Handle(ResolveSosIncidentCommand cmd, CancellationToken ct)
    {
        if (!ValidOutcomes.Contains(cmd.Outcome))
            return new(false, "INVALID_OUTCOME");

        var incident = await db.SosIncidents.FirstOrDefaultAsync(s => s.Id == cmd.IncidentId, ct);
        if (incident is null) return new(false, "INCIDENT_NOT_FOUND");
        if (incident.Status == SosStatus.Resolved) return new(false, "ALREADY_RESOLVED");

        var before = new { incident.Status, incident.Outcome };
        incident.Status  = SosStatus.Resolved;
        incident.Outcome = cmd.Outcome.ToLower();
        incident.Notes   = cmd.Notes;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.SosResolve,
            "SosIncident", cmd.IncidentId, reason: cmd.Outcome,
            before: before, after: new { Status = "resolved", Outcome = cmd.Outcome }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/safety/incidents/{id}/escalate ────────────────────────────────
// authority (name), reference (case / reference number), notes.

public record EscalateSosIncidentCommand(string IncidentId, string Authority,
    string? Reference, string? Notes, string StaffId, string StaffName)
    : IRequest<SafetyCommandResult>;

public class EscalateSosIncidentHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<EscalateSosIncidentCommand, SafetyCommandResult>
{
    public async Task<SafetyCommandResult> Handle(EscalateSosIncidentCommand cmd, CancellationToken ct)
    {
        var incident = await db.SosIncidents.FirstOrDefaultAsync(s => s.Id == cmd.IncidentId, ct);
        if (incident is null) return new(false, "INCIDENT_NOT_FOUND");
        if (incident.Status == SosStatus.Resolved) return new(false, "ALREADY_RESOLVED");

        var before = new { incident.Status, incident.AuthorityName, incident.AuthorityReference };
        incident.Status             = SosStatus.Escalated;
        incident.AuthorityName      = cmd.Authority;
        incident.AuthorityReference = cmd.Reference;
        incident.Notes              = cmd.Notes;
        incident.Outcome            = "escalated";

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.SosEscalate,
            "SosIncident", cmd.IncidentId, reason: $"Escalated to {cmd.Authority}",
            before: before,
            after: new { Status = "escalated", Authority = cmd.Authority, Reference = cmd.Reference },
            ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}
