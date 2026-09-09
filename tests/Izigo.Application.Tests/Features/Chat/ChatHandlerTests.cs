using FluentAssertions;
using Izigo.Application.Features.Chat.Commands;
using Izigo.Application.Features.Chat.Dtos;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Enums;
using Xunit;
using TripEntity = Izigo.Domain.Entities.Trip;
using ConversationEntity = Izigo.Domain.Entities.Conversation;
using MessageEntity = Izigo.Domain.Entities.Message;

namespace Izigo.Application.Tests.Features.Chat;

public class ChatHandlerTests
{
    private static TripEntity BuildTrip(string riderId, string? driverId = null) => new()
    {
        Code = "ZR-0001", RiderId = riderId, DriverId = driverId,
        Market = "ci", Currency = "XOF", Vertical = Vertical.Ride,
        ServiceClass = ServiceClass.ZenCar, JobState = JobState.Accepted,
        PickupLat = 5.3m, PickupLng = -4.0m, PickupLabel = "A",
        DropoffLat = 5.4m, DropoffLng = -4.1m, DropoffLabel = "B",
        PaymentMethod = PaymentMethod.Cash
    };

    // ── GetOrCreateTripConversationHandler ────────────────────────────────────

    [Fact]
    public async Task GetOrCreateConversation_TripNotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new GetOrCreateTripConversationHandler(db);

        var act = () => handler.Handle(
            new GetOrCreateTripConversationCommand("user1", "nonexistent"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GetOrCreateConversation_NoExisting_CreatesNew()
    {
        using var db = DbContextFactory.Create();
        var riderId = Guid.NewGuid().ToString();
        var trip = BuildTrip(riderId);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new GetOrCreateTripConversationHandler(db);
        var result  = await handler.Handle(
            new GetOrCreateTripConversationCommand(riderId, trip.Id), CancellationToken.None);

        result.Kind.Should().Be("trip");
        result.TripId.Should().Be(trip.Id);
        db.Conversations.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetOrCreateConversation_ExistingConversation_ReturnsIt()
    {
        using var db = DbContextFactory.Create();
        var riderId = Guid.NewGuid().ToString();
        var trip = BuildTrip(riderId);
        db.Trips.Add(trip);
        var conv = new ConversationEntity
        {
            Kind = ConversationKind.Trip, TripId = trip.Id
        };
        db.Conversations.Add(conv);
        trip.Conversation = conv;
        await db.SaveChangesAsync();

        var handler = new GetOrCreateTripConversationHandler(db);
        var result  = await handler.Handle(
            new GetOrCreateTripConversationCommand(riderId, trip.Id), CancellationToken.None);

        result.Id.Should().Be(conv.Id);
        db.Conversations.Should().HaveCount(1); // no new one created
    }

    [Fact]
    public async Task GetOrCreateConversation_DriverCanAccess()
    {
        using var db = DbContextFactory.Create();
        var riderId  = Guid.NewGuid().ToString();
        var driverId = Guid.NewGuid().ToString();
        var trip = BuildTrip(riderId, driverId);
        db.Trips.Add(trip);
        await db.SaveChangesAsync();

        var handler = new GetOrCreateTripConversationHandler(db);
        var result  = await handler.Handle(
            new GetOrCreateTripConversationCommand(driverId, trip.Id), CancellationToken.None);

        result.TripId.Should().Be(trip.Id);
    }

    // ── SendMessageHandler ────────────────────────────────────────────────────

    [Fact]
    public async Task SendMessage_ConversationNotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new SendMessageHandler(db, FakeServices.Realtime());

        var act = () => handler.Handle(
            new SendMessageCommand("sender1", "rider", "nonexistent",
                new SendMessageRequest("text", "Hello", null, null, null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task SendMessage_InvalidType_ThrowsArgumentException()
    {
        using var db = DbContextFactory.Create();
        var riderId = Guid.NewGuid().ToString();
        var trip = BuildTrip(riderId);
        db.Trips.Add(trip);
        var conv = new ConversationEntity { Kind = ConversationKind.Trip, TripId = trip.Id };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync();

        var handler = new SendMessageHandler(db, FakeServices.Realtime());
        var act = () => handler.Handle(
            new SendMessageCommand(riderId, "rider", conv.Id,
                new SendMessageRequest("invalid_type", "Hello", null, null, null)),
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*message type*");
    }

    [Fact]
    public async Task SendMessage_ValidText_PersistsAndReturnsDto()
    {
        using var db = DbContextFactory.Create();
        var riderId = Guid.NewGuid().ToString();
        var trip = BuildTrip(riderId);
        db.Trips.Add(trip);
        var conv = new ConversationEntity { Kind = ConversationKind.Trip, TripId = trip.Id };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync();

        var handler = new SendMessageHandler(db, FakeServices.Realtime());
        var result  = await handler.Handle(
            new SendMessageCommand(riderId, "rider", conv.Id,
                new SendMessageRequest("text", "Hi driver!", null, null, "local-id-1")),
            CancellationToken.None);

        result.Body.Should().Be("Hi driver!");
        result.SenderRole.Should().Be("rider");
        db.Messages.Should().HaveCount(1);
    }

    [Fact]
    public async Task SendMessage_DuplicateLocalId_ReturnsExistingMessage()
    {
        using var db = DbContextFactory.Create();
        var riderId = Guid.NewGuid().ToString();
        var trip = BuildTrip(riderId);
        db.Trips.Add(trip);
        var conv = new ConversationEntity { Kind = ConversationKind.Trip, TripId = trip.Id };
        db.Conversations.Add(conv);
        var existing = new MessageEntity
        {
            ConversationId = conv.Id,
            SenderId = riderId, SenderRole = "rider",
            Type = MessageType.Text, Body = "Original",
            LocalId = "dup-local-id"
        };
        db.Messages.Add(existing);
        await db.SaveChangesAsync();

        var handler = new SendMessageHandler(db, FakeServices.Realtime());
        var result  = await handler.Handle(
            new SendMessageCommand(riderId, "rider", conv.Id,
                new SendMessageRequest("text", "Duplicate attempt", null, null, "dup-local-id")),
            CancellationToken.None);

        result.Id.Should().Be(existing.Id); // returns existing, not new
        db.Messages.Should().HaveCount(1);  // no duplicate created
    }

    // ── MarkConversationReadHandler ───────────────────────────────────────────

    [Fact(Skip = "MarkConversationReadHandler uses ExecuteUpdateAsync — not supported by InMemory EF. Covered by integration tests.")]
    public async Task MarkConversationRead_DoesNotThrow()
    {
        // ExecuteUpdateAsync is a bulk SQL operation — not supported by the InMemory EF provider.
        // This test verifies the handler executes without error given valid input.
        using var db = DbContextFactory.Create();
        var riderId  = Guid.NewGuid().ToString();
        var driverId = Guid.NewGuid().ToString();
        var conv = new ConversationEntity { Kind = ConversationKind.Trip };
        db.Conversations.Add(conv);
        db.Messages.AddRange(
            new MessageEntity { ConversationId = conv.Id, SenderId = driverId,
                SenderRole = "driver", Type = MessageType.Text, Body = "Hello" },
            new MessageEntity { ConversationId = conv.Id, SenderId = riderId,
                SenderRole = "rider", Type = MessageType.Text, Body = "Reply" });
        await db.SaveChangesAsync();

        var handler = new MarkConversationReadHandler(db);
        await handler.Invoking(h => h.Handle(
            new MarkConversationReadCommand(riderId, conv.Id), CancellationToken.None))
            .Should().NotThrowAsync();
    }
}
