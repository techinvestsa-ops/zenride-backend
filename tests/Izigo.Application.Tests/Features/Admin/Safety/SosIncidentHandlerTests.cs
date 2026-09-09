using FluentAssertions;
using Izigo.Application.Features.Admin.Safety.Commands;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Xunit;

namespace Izigo.Application.Tests.Features.Admin.Safety;

public class SosIncidentHandlerTests
{
    private static SosIncident BuildIncident(SosStatus status = SosStatus.Active) => new()
    {
        UserId = "usr_1",
        Source = "rider",
        Type   = SosType.Accident,
        Status = status,
        Lat    = 5.35m,
        Lng    = -3.99m,
        Market = "ci"
    };

    [Fact]
    public async Task Acknowledge_SetsOwnerAndTimestamp()
    {
        using var db = DbContextFactory.Create();
        var incident = BuildIncident();
        db.SosIncidents.Add(incident);
        await db.SaveChangesAsync();

        var handler = new AcknowledgeSosIncidentHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new AcknowledgeSosIncidentCommand(incident.Id, "stf_1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var updated = db.SosIncidents.First(s => s.Id == incident.Id);
        updated.AcknowledgedByStaffId.Should().Be("stf_1");
        updated.AcknowledgedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Acknowledge_AlreadyAcknowledged_ReturnsError()
    {
        using var db = DbContextFactory.Create();
        var incident = BuildIncident();
        incident.AcknowledgedByStaffId = "stf_prev";
        incident.AcknowledgedAt        = DateTime.UtcNow.AddMinutes(-5);
        db.SosIncidents.Add(incident);
        await db.SaveChangesAsync();

        var handler = new AcknowledgeSosIncidentHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new AcknowledgeSosIncidentCommand(incident.Id, "stf_1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("ALREADY_ACKNOWLEDGED");
    }

    [Fact]
    public async Task Resolve_WithInvalidOutcome_ReturnsError()
    {
        using var db = DbContextFactory.Create();
        var incident = BuildIncident();
        db.SosIncidents.Add(incident);
        await db.SaveChangesAsync();

        var handler = new ResolveSosIncidentHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new ResolveSosIncidentCommand(incident.Id, "bad_outcome", null, "stf_1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_OUTCOME");
    }

    [Fact]
    public async Task Resolve_ValidOutcome_SetsStatusAndOutcome()
    {
        using var db = DbContextFactory.Create();
        var incident = BuildIncident();
        db.SosIncidents.Add(incident);
        await db.SaveChangesAsync();

        var handler = new ResolveSosIncidentHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new ResolveSosIncidentCommand(incident.Id, "false_alarm", "Rider confirmed safe", "stf_1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var updated = db.SosIncidents.First(s => s.Id == incident.Id);
        updated.Status.Should().Be(SosStatus.Resolved);
        updated.Outcome.Should().Be("false_alarm");
    }

    [Fact]
    public async Task Escalate_SetsAuthorityAndStatus()
    {
        using var db = DbContextFactory.Create();
        var incident = BuildIncident();
        db.SosIncidents.Add(incident);
        await db.SaveChangesAsync();

        var handler = new EscalateSosIncidentHandler(db, FakeServices.Audit());
        var result  = await handler.Handle(
            new EscalateSosIncidentCommand(incident.Id, "Police Abidjan", "REF-2025-001", "Urgent", "stf_1", "Admin"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        var updated = db.SosIncidents.First(s => s.Id == incident.Id);
        updated.Status.Should().Be(SosStatus.Escalated);
        updated.AuthorityName.Should().Be("Police Abidjan");
        updated.AuthorityReference.Should().Be("REF-2025-001");
    }
}
