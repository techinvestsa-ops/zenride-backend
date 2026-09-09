using Izigo.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Notifications.Commands;

// ── POST /notifications/{id}/read ────────────────────────────────────────────

public record MarkNotificationReadCommand(string UserId, string NotificationId) : IRequest;

public class MarkNotificationReadHandler(IApplicationDbContext db)
    : IRequestHandler<MarkNotificationReadCommand>
{
    public async Task Handle(MarkNotificationReadCommand cmd, CancellationToken ct)
    {
        var notification = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == cmd.NotificationId && n.UserId == cmd.UserId, ct)
            ?? throw new KeyNotFoundException("Notification not found.");

        notification.IsRead = true;
        await db.SaveChangesAsync(ct);
    }
}

// ── POST /notifications/read-all ─────────────────────────────────────────────

public record MarkAllReadCommand(string UserId) : IRequest;

public class MarkAllReadHandler(IApplicationDbContext db)
    : IRequestHandler<MarkAllReadCommand>
{
    public async Task Handle(MarkAllReadCommand cmd, CancellationToken ct)
    {
        await db.Notifications
            .Where(n => n.UserId == cmd.UserId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true), ct);
    }
}

// ── DELETE /notifications/{id} ────────────────────────────────────────────────

public record DeleteNotificationCommand(string UserId, string NotificationId) : IRequest;

public class DeleteNotificationHandler(IApplicationDbContext db)
    : IRequestHandler<DeleteNotificationCommand>
{
    public async Task Handle(DeleteNotificationCommand cmd, CancellationToken ct)
    {
        var notification = await db.Notifications
            .FirstOrDefaultAsync(n => n.Id == cmd.NotificationId && n.UserId == cmd.UserId, ct)
            ?? throw new KeyNotFoundException("Notification not found.");

        db.Notifications.Remove(notification);
        await db.SaveChangesAsync(ct);
    }
}
