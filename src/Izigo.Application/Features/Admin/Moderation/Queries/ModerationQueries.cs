using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Moderation.Queries;

// ── GET /admin/moderation/log ─────────────────────────────────────────────────
// Subset of the audit log filtered to moderation actions.
// Filter by: actor, target_type, action, date.

public record GetModerationLogQuery(
    string? Actor, string? TargetType, string? Action,
    DateTime? From, DateTime? To,
    int Page, int PerPage) : IRequest<object>;

public class GetModerationLogHandler(IApplicationDbContext db)
    : IRequestHandler<GetModerationLogQuery, object>
{
    // Actions that constitute moderation activity
    private static readonly HashSet<AuditAction> ModerationActions =
    [
        AuditAction.RiderSuspend,
        AuditAction.RiderBlock,
        AuditAction.RiderUnblock,
        AuditAction.RiderDelete,
        AuditAction.RiderFlag,
        AuditAction.RiderLogout,
        AuditAction.DriverSuspend,
        AuditAction.DriverBlock,
        AuditAction.DriverUnblock,
        AuditAction.DriverLogout,
        AuditAction.DriverPerformanceReset,
        AuditAction.PasswordResetLinkSent,
    ];

    public async Task<object> Handle(GetModerationLogQuery req, CancellationToken ct)
    {
        var query = db.AuditLogs
            .Where(a => ModerationActions.Contains(a.Action))
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(req.Actor))
            query = query.Where(a => a.ActorId == req.Actor || a.ActorName.Contains(req.Actor));

        if (!string.IsNullOrWhiteSpace(req.TargetType))
            query = query.Where(a => a.TargetType == req.TargetType);

        if (!string.IsNullOrWhiteSpace(req.Action) &&
            Enum.TryParse<AuditAction>(req.Action, true, out var action))
            query = query.Where(a => a.Action == action);

        if (req.From.HasValue) query = query.Where(a => a.CreatedAt >= req.From.Value);
        if (req.To.HasValue)   query = query.Where(a => a.CreatedAt <= req.To.Value);

        var perPage  = Math.Max(1, Math.Min(100, req.PerPage));
        var total    = await query.CountAsync(ct);
        var lastPage = (int)Math.Ceiling((double)total / perPage);

        var entries = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((req.Page - 1) * perPage)
            .Take(perPage)
            .Select(a => new
            {
                a.Id,
                At      = a.CreatedAt,
                Actor   = new { a.ActorId, a.ActorName },
                Action  = a.Action.ToString(),
                Target  = new { Type = a.TargetType, Id = a.TargetId },
                a.IpAddress,
                a.Reason
            })
            .ToListAsync(ct);

        return AdminApiResponse.Ok(entries, new AdminPagedMeta(req.Page, perPage, total, lastPage));
    }
}
