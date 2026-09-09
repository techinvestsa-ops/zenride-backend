using FluentAssertions;
using Izigo.Application.Features.Support.Commands;
using Izigo.Application.Features.Support.Dtos;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Enums;
using Xunit;
using TicketEntity = Izigo.Domain.Entities.SupportTicket;

namespace Izigo.Application.Tests.Features.Support;

public class SupportHandlerTests
{
    private static TicketEntity BuildTicket(string userId, TicketStatus status = TicketStatus.Open) => new()
    {
        UserId      = userId,
        UserRole    = "rider",
        Category    = "trip_issue",
        Description = "My trip was cancelled unfairly.",
        Status      = status,
        Priority    = TicketPriority.Medium,
        Reference   = "TKT-ABC123",
        Market      = "ci"
    };

    // ── CreateTicketHandler ───────────────────────────────────────────────────

    [Fact]
    public async Task CreateTicket_Success_CreatesTicketAndOpeningMessage()
    {
        using var db = DbContextFactory.Create();
        var userId  = Guid.NewGuid().ToString();
        var handler = new CreateTicketHandler(db);

        var result = await handler.Handle(
            new CreateTicketCommand(userId, "rider",
                new CreateTicketRequest("payment_issue", "I was charged twice.", null, null)),
            CancellationToken.None);

        result.Should().NotBeNull();
        result.Category.Should().Be("payment_issue");
        result.Status.Should().Be("open");
        db.SupportTickets.Should().HaveCount(1);
        db.SupportTicketMessages.Should().HaveCount(1);
    }

    [Fact]
    public async Task CreateTicket_ReferencePrefixedWithTkt()
    {
        using var db = DbContextFactory.Create();
        var handler = new CreateTicketHandler(db);

        var result = await handler.Handle(
            new CreateTicketCommand(Guid.NewGuid().ToString(), "rider",
                new CreateTicketRequest("cat", "Desc1", null, null)),
            CancellationToken.None);

        result.Reference.Should().StartWith("TKT-");
        result.Reference.Length.Should().BeGreaterThanOrEqualTo(8);
    }

    // ── ReplyTicketHandler ────────────────────────────────────────────────────

    [Fact]
    public async Task ReplyTicket_NotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new ReplyTicketHandler(db);

        var act = () => handler.Handle(
            new ReplyTicketCommand("user1", "nonexistent",
                new ReplyTicketRequest("Hello?", null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task ReplyTicket_ClosedTicket_ThrowsConflict()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        var ticket = BuildTicket(userId, TicketStatus.Closed);
        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync();

        var handler = new ReplyTicketHandler(db);
        var act = () => handler.Handle(
            new ReplyTicketCommand(userId, ticket.Id,
                new ReplyTicketRequest("Can I reply?", null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*closed ticket*");
    }

    [Fact]
    public async Task ReplyTicket_ResolvedTicket_ReopensTicket()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        var ticket = BuildTicket(userId, TicketStatus.Resolved);
        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync();

        var handler = new ReplyTicketHandler(db);
        await handler.Handle(
            new ReplyTicketCommand(userId, ticket.Id,
                new ReplyTicketRequest("Still having issues.", null)),
            CancellationToken.None);

        db.SupportTickets.Find(ticket.Id)!.Status.Should().Be(TicketStatus.Open);
        db.SupportTicketMessages.Should().HaveCount(1);
    }

    [Fact]
    public async Task ReplyTicket_OpenTicket_AddsMessage()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        var ticket = BuildTicket(userId, TicketStatus.Open);
        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync();

        var handler = new ReplyTicketHandler(db);
        var result  = await handler.Handle(
            new ReplyTicketCommand(userId, ticket.Id,
                new ReplyTicketRequest("Following up.", null)),
            CancellationToken.None);

        result.Body.Should().Be("Following up.");
        result.SenderType.Should().Be("user");
        db.SupportTicketMessages.Should().HaveCount(1);
    }

    // ── CloseTicketHandler ────────────────────────────────────────────────────

    [Fact]
    public async Task CloseTicket_NotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new CloseTicketHandler(db);

        var act = () => handler.Handle(
            new CloseTicketCommand("user1", "nonexistent"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task CloseTicket_OpenTicket_ClosesIt()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        var ticket = BuildTicket(userId, TicketStatus.Open);
        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync();

        var handler = new CloseTicketHandler(db);
        await handler.Handle(
            new CloseTicketCommand(userId, ticket.Id), CancellationToken.None);

        db.SupportTickets.Find(ticket.Id)!.Status.Should().Be(TicketStatus.Closed);
    }

    [Fact]
    public async Task CloseTicket_AlreadyClosed_IsIdempotent()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        var ticket = BuildTicket(userId, TicketStatus.Closed);
        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync();

        var handler = new CloseTicketHandler(db);
        // Should not throw — idempotent
        await handler.Invoking(h => h.Handle(
            new CloseTicketCommand(userId, ticket.Id), CancellationToken.None))
            .Should().NotThrowAsync();
    }
}
