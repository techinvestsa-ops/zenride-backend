using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Notifications.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Notifications.Queries;

// ── GET /notifications ────────────────────────────────────────────────────────

public record GetNotificationsQuery(string UserId, string? Group, int Page, int PerPage)
    : IRequest<(List<NotificationDto> Items, int Total)>;

public class GetNotificationsHandler(IApplicationDbContext db)
    : IRequestHandler<GetNotificationsQuery, (List<NotificationDto>, int)>
{
    public async Task<(List<NotificationDto>, int)> Handle(
        GetNotificationsQuery req, CancellationToken ct)
    {
        var q = db.Notifications.Where(n => n.UserId == req.UserId);

        if (!string.IsNullOrEmpty(req.Group) &&
            Enum.TryParse<Domain.Enums.NotificationGroup>(req.Group, true, out var group))
            q = q.Where(n => n.Group == group);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(n => n.CreatedAt)
            .Skip((req.Page - 1) * req.PerPage)
            .Take(req.PerPage)
            .Select(n => new NotificationDto(
                n.Id, n.Type, n.Title, n.Body, n.ImageUrl, n.DeepLink,
                n.IsRead, n.Group.ToString().ToLower(), n.CreatedAt))
            .ToListAsync(ct);

        return (items, total);
    }
}

// ── GET /notifications/unread-count ──────────────────────────────────────────

public record GetUnreadCountQuery(string UserId) : IRequest<UnreadCountDto>;

public class GetUnreadCountHandler(IApplicationDbContext db)
    : IRequestHandler<GetUnreadCountQuery, UnreadCountDto>
{
    public async Task<UnreadCountDto> Handle(GetUnreadCountQuery req, CancellationToken ct)
    {
        var count = await db.Notifications
            .CountAsync(n => n.UserId == req.UserId && !n.IsRead, ct);
        return new UnreadCountDto(count);
    }
}
