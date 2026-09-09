using FluentAssertions;
using Izigo.Application.Features.Notifications.Commands;
using Izigo.Application.Features.Notifications.Queries;
using Izigo.Application.Tests.Helpers;
using Izigo.Domain.Enums;
using Xunit;
using NotificationEntity = Izigo.Domain.Entities.Notification;

namespace Izigo.Application.Tests.Features.Notifications;

public class NotificationHandlerTests
{
    private static NotificationEntity BuildNotification(string userId, bool isRead = false) => new()
    {
        UserId = userId,
        Type   = "ride.completed",
        Title  = "Trip completed",
        Body   = "Your trip is done.",
        Group  = NotificationGroup.Trip,
        IsRead = isRead
    };

    // ── GetNotificationsHandler ───────────────────────────────────────────────

    [Fact]
    public async Task GetNotifications_NoNotifications_ReturnsEmpty()
    {
        using var db = DbContextFactory.Create();
        var handler = new GetNotificationsHandler(db);

        var (items, total) = await handler.Handle(
            new GetNotificationsQuery(Guid.NewGuid().ToString(), null, 1, 20),
            CancellationToken.None);

        items.Should().BeEmpty();
        total.Should().Be(0);
    }

    [Fact]
    public async Task GetNotifications_ReturnsOnlyUserNotifications()
    {
        using var db = DbContextFactory.Create();
        var userId  = Guid.NewGuid().ToString();
        var otherId = Guid.NewGuid().ToString();
        db.Notifications.AddRange(
            BuildNotification(userId),
            BuildNotification(userId),
            BuildNotification(otherId));
        await db.SaveChangesAsync();

        var handler = new GetNotificationsHandler(db);
        var (items, total) = await handler.Handle(
            new GetNotificationsQuery(userId, null, 1, 20), CancellationToken.None);

        items.Should().HaveCount(2);
        total.Should().Be(2);
    }

    [Fact]
    public async Task GetNotifications_FilterByGroup_ReturnsMatchingOnly()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        var tripNotif = BuildNotification(userId);
        tripNotif.Group = NotificationGroup.Trip;
        var promoNotif = BuildNotification(userId);
        promoNotif.Group = NotificationGroup.Promo;
        db.Notifications.AddRange(tripNotif, promoNotif);
        await db.SaveChangesAsync();

        var handler = new GetNotificationsHandler(db);
        var (items, total) = await handler.Handle(
            new GetNotificationsQuery(userId, "Trip", 1, 20), CancellationToken.None);

        items.Should().HaveCount(1);
        items[0].Group.Should().Be("trip");
        total.Should().Be(1);
    }

    [Fact]
    public async Task GetNotifications_Pagination_RespectsPerPage()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        for (var i = 0; i < 5; i++)
            db.Notifications.Add(BuildNotification(userId));
        await db.SaveChangesAsync();

        var handler = new GetNotificationsHandler(db);
        var (items, total) = await handler.Handle(
            new GetNotificationsQuery(userId, null, 1, 3), CancellationToken.None);

        items.Should().HaveCount(3);
        total.Should().Be(5);
    }

    // ── GetUnreadCountHandler ─────────────────────────────────────────────────

    [Fact]
    public async Task GetUnreadCount_CountsUnreadOnly()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        db.Notifications.AddRange(
            BuildNotification(userId, isRead: false),
            BuildNotification(userId, isRead: false),
            BuildNotification(userId, isRead: true));
        await db.SaveChangesAsync();

        var handler = new GetUnreadCountHandler(db);
        var result  = await handler.Handle(
            new GetUnreadCountQuery(userId), CancellationToken.None);

        result.Count.Should().Be(2);
    }

    // ── MarkNotificationReadHandler ───────────────────────────────────────────

    [Fact]
    public async Task MarkNotificationRead_NotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new MarkNotificationReadHandler(db);

        var act = () => handler.Handle(
            new MarkNotificationReadCommand("user1", "nonexistent"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task MarkNotificationRead_WrongUser_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var notif = BuildNotification("user-A");
        db.Notifications.Add(notif);
        await db.SaveChangesAsync();

        var handler = new MarkNotificationReadHandler(db);
        var act = () => handler.Handle(
            new MarkNotificationReadCommand("user-B", notif.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task MarkNotificationRead_Success_SetsIsRead()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        var notif  = BuildNotification(userId, isRead: false);
        db.Notifications.Add(notif);
        await db.SaveChangesAsync();

        var handler = new MarkNotificationReadHandler(db);
        await handler.Handle(
            new MarkNotificationReadCommand(userId, notif.Id), CancellationToken.None);

        db.Notifications.Find(notif.Id)!.IsRead.Should().BeTrue();
    }

    // ── MarkAllReadHandler ────────────────────────────────────────────────────

    [Fact(Skip = "MarkAllReadHandler uses ExecuteUpdateAsync — not supported by InMemory EF. Covered by integration tests.")]
    public async Task MarkAllRead_DoesNotThrow()
    {
        // ExecuteUpdateAsync is a bulk SQL operation — not supported by the InMemory EF provider.
        // This test verifies the handler executes without error given valid input.
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        db.Notifications.AddRange(
            BuildNotification(userId, false),
            BuildNotification(userId, false));
        await db.SaveChangesAsync();

        var handler = new MarkAllReadHandler(db);
        await handler.Invoking(h => h.Handle(
            new MarkAllReadCommand(userId), CancellationToken.None))
            .Should().NotThrowAsync();
    }

    // ── DeleteNotificationHandler ─────────────────────────────────────────────

    [Fact]
    public async Task DeleteNotification_NotFound_ThrowsKeyNotFound()
    {
        using var db = DbContextFactory.Create();
        var handler = new DeleteNotificationHandler(db);

        var act = () => handler.Handle(
            new DeleteNotificationCommand("user1", "nonexistent"), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task DeleteNotification_Success_RemovesNotification()
    {
        using var db = DbContextFactory.Create();
        var userId = Guid.NewGuid().ToString();
        var notif  = BuildNotification(userId);
        db.Notifications.Add(notif);
        await db.SaveChangesAsync();

        var handler = new DeleteNotificationHandler(db);
        await handler.Handle(
            new DeleteNotificationCommand(userId, notif.Id), CancellationToken.None);

        db.Notifications.Should().BeEmpty();
    }
}
