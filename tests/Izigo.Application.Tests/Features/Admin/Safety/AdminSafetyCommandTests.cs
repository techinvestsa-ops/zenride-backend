using FluentAssertions;
using Izigo.Application.Features.Admin.Safety.Commands;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Admin.Safety;

public class AdminSafetyCommandTests
{
    private static SosIncident BuildIncident(SosStatus status = SosStatus.Active) => new()
    {
        Type     = SosType.Other,
        Source   = "button",
        Status   = status,
        Market   = "ci",
        UserId   = Guid.NewGuid().ToString(),
        Lat      = 5.35m,
        Lng      = -4.00m,
        CreatedAt = DateTime.UtcNow
    };

    // ── AcknowledgeSosIncidentHandler ─────────────────────────────────────────

    [Fact]
    public async Task AcknowledgeIncident_NotFound_ReturnsError()
    {
        using var db = DbContextFactory.Create();
        var handler = new AcknowledgeSosIncidentHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new AcknowledgeSosIncidentCommand("nonexistent", "staff1", "Staff"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INCIDENT_NOT_FOUND");
    }

    [Fact]
    public async Task AcknowledgeIncident_AlreadyAcknowledged_ReturnsError()
    {
        using var db = DbContextFactory.Create();
        var incident = BuildIncident();
        incident.AcknowledgedAt        = DateTime.UtcNow.AddMinutes(-5);
        incident.AcknowledgedByStaffId = "staff-prev";
        db.SosIncidents.Add(incident);
        await db.SaveChangesAsync();

        var handler = new AcknowledgeSosIncidentHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new AcknowledgeSosIncidentCommand(incident.Id, "staff1", "Staff"),
            CancellationToken.None);

        result.ErrorCode.Should().Be("ALREADY_ACKNOWLEDGED");
    }

    [Fact]
    public async Task AcknowledgeIncident_Success_SetsTimestampAndOwner()
    {
        using var db = DbContextFactory.Create();
        var incident = BuildIncident();
        db.SosIncidents.Add(incident);
        await db.SaveChangesAsync();

        var before  = DateTime.UtcNow;
        var handler = new AcknowledgeSosIncidentHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new AcknowledgeSosIncidentCommand(incident.Id, "staff1", "Staff One"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var updated = db.SosIncidents.First(s => s.Id == incident.Id);
        updated.AcknowledgedByStaffId.Should().Be("staff1");
        updated.AcknowledgedAt.Should().NotBeNull();
        updated.AcknowledgedAt!.Value.Should().BeAfter(before.AddSeconds(-1));
    }

    // ── ResolveSosIncidentHandler ─────────────────────────────────────────────

    [Fact]
    public async Task ResolveIncident_NotFound_ReturnsError()
    {
        using var db = DbContextFactory.Create();
        var handler = new ResolveSosIncidentHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new ResolveSosIncidentCommand("nonexistent", "resolved", null, "staff1", "Staff"),
            CancellationToken.None);

        result.ErrorCode.Should().Be("INCIDENT_NOT_FOUND");
    }

    [Fact]
    public async Task ResolveIncident_AlreadyResolved_ReturnsError()
    {
        using var db = DbContextFactory.Create();
        var incident = BuildIncident(SosStatus.Resolved);
        db.SosIncidents.Add(incident);
        await db.SaveChangesAsync();

        var handler = new ResolveSosIncidentHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new ResolveSosIncidentCommand(incident.Id, "resolved", null, "staff1", "Staff"),
            CancellationToken.None);

        result.ErrorCode.Should().Be("ALREADY_RESOLVED");
    }

    [Fact]
    public async Task ResolveIncident_InvalidOutcome_ReturnsError()
    {
        using var db = DbContextFactory.Create();
        var incident = BuildIncident();
        db.SosIncidents.Add(incident);
        await db.SaveChangesAsync();

        var handler = new ResolveSosIncidentHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new ResolveSosIncidentCommand(incident.Id, "invalid_outcome", null, "staff1", "Staff"),
            CancellationToken.None);

        result.ErrorCode.Should().Be("INVALID_OUTCOME");
    }

    [Theory]
    [InlineData("resolved")]
    [InlineData("false_alarm")]
    [InlineData("escalated")]
    public async Task ResolveIncident_ValidOutcomes_Succeed(string outcome)
    {
        using var db = DbContextFactory.Create();
        var incident = BuildIncident();
        db.SosIncidents.Add(incident);
        await db.SaveChangesAsync();

        var handler = new ResolveSosIncidentHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new ResolveSosIncidentCommand(incident.Id, outcome, "Notes here", "staff1", "Staff"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var updated = db.SosIncidents.First(s => s.Id == incident.Id);
        updated.Status.Should().Be(SosStatus.Resolved);
        updated.Outcome.Should().Be(outcome);
    }

    // ── EscalateSosIncidentHandler ────────────────────────────────────────────

    [Fact]
    public async Task EscalateIncident_NotFound_ReturnsError()
    {
        using var db = DbContextFactory.Create();
        var handler = new EscalateSosIncidentHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new EscalateSosIncidentCommand("nonexistent", "Police", null, null, "staff1", "Staff"),
            CancellationToken.None);

        result.ErrorCode.Should().Be("INCIDENT_NOT_FOUND");
    }

    [Fact]
    public async Task EscalateIncident_AlreadyResolved_ReturnsError()
    {
        using var db = DbContextFactory.Create();
        var incident = BuildIncident(SosStatus.Resolved);
        db.SosIncidents.Add(incident);
        await db.SaveChangesAsync();

        var handler = new EscalateSosIncidentHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new EscalateSosIncidentCommand(incident.Id, "Police", "REF-001", null, "staff1", "Staff"),
            CancellationToken.None);

        result.ErrorCode.Should().Be("ALREADY_RESOLVED");
    }

    [Fact]
    public async Task EscalateIncident_Success_SetsEscalatedStatusAndAuthority()
    {
        using var db = DbContextFactory.Create();
        var incident = BuildIncident();
        db.SosIncidents.Add(incident);
        await db.SaveChangesAsync();

        var handler = new EscalateSosIncidentHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new EscalateSosIncidentCommand(incident.Id, "National Police", "REF-2025-001",
                "Urgent situation", "staff1", "Staff One"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var updated = db.SosIncidents.First(s => s.Id == incident.Id);
        updated.Status.Should().Be(SosStatus.Escalated);
        updated.AuthorityName.Should().Be("National Police");
        updated.AuthorityReference.Should().Be("REF-2025-001");
        updated.Outcome.Should().Be("escalated");
    }
}
