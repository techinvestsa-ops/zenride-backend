using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.AuditLog.Queries;

// ── DTOs ─────────────────────────────────────────────────────────────────────────

public record AuditActorDto(string Id, string Name);
public record AuditTargetDto(string? Type, string? Id);

public record AuditLogEntryDto(
    string Id,
    DateTime At,
    AuditActorDto Actor,
    string Action,
    AuditTargetDto Target,
    string? IpAddress,
    string? RequestId,
    string? Reason);

public record AuditLogDetailDto(
    string Id,
    DateTime At,
    AuditActorDto Actor,
    string Action,
    AuditTargetDto Target,
    string? IpAddress,
    string? RequestId,
    string? Reason,
    string? BeforeJson,
    string? AfterJson);

// ── LIST ─────────────────────────────────────────────────────────────────────────

public record GetAuditLogQuery(
    string? Actor,
    string? Action,
    string? TargetType,
    string? TargetId,
    DateTime? From,
    DateTime? To,
    string? Q,
    int Page = 1,
    int PerPage = 25) : IRequest<object>;

public class GetAuditLogHandler(IApplicationDbContext db)
    : IRequestHandler<GetAuditLogQuery, object>
{
    public async Task<object> Handle(GetAuditLogQuery req, CancellationToken ct)
    {
        var query = db.AuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(req.Actor))
            query = query.Where(a => a.ActorId == req.Actor || a.ActorName.Contains(req.Actor));

        if (!string.IsNullOrWhiteSpace(req.Action) &&
            Enum.TryParse<AuditAction>(req.Action, true, out var action))
            query = query.Where(a => a.Action == action);

        if (!string.IsNullOrWhiteSpace(req.TargetType))
            query = query.Where(a => a.TargetType == req.TargetType);

        if (!string.IsNullOrWhiteSpace(req.TargetId))
            query = query.Where(a => a.TargetId == req.TargetId);

        if (req.From.HasValue)
            query = query.Where(a => a.CreatedAt >= req.From.Value);

        if (req.To.HasValue)
            query = query.Where(a => a.CreatedAt <= req.To.Value);

        if (!string.IsNullOrWhiteSpace(req.Q))
            query = query.Where(a => a.ActorName.Contains(req.Q) || (a.Reason != null && a.Reason.Contains(req.Q)));

        var perPage  = Math.Max(1, Math.Min(100, req.PerPage));
        var total    = await query.CountAsync(ct);
        var lastPage = (int)Math.Ceiling((double)total / perPage);

        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .Skip((req.Page - 1) * perPage)
            .Take(perPage)
            .Select(a => new AuditLogEntryDto(
                a.Id,
                a.CreatedAt,
                new AuditActorDto(a.ActorId, a.ActorName),
                a.Action.ToString(),
                new AuditTargetDto(a.TargetType, a.TargetId),
                a.IpAddress,
                a.RequestId,
                a.Reason))
            .ToListAsync(ct);

        var meta = new AdminPagedMeta(req.Page, perPage, total, lastPage);
        return AdminApiResponse.Ok(items, meta);
    }
}

// ── DETAIL ────────────────────────────────────────────────────────────────────────

public record GetAuditLogEntryQuery(string Id) : IRequest<AuditLogDetailDto?>;

public class GetAuditLogEntryHandler(IApplicationDbContext db)
    : IRequestHandler<GetAuditLogEntryQuery, AuditLogDetailDto?>
{
    public async Task<AuditLogDetailDto?> Handle(GetAuditLogEntryQuery req, CancellationToken ct)
    {
        var a = await db.AuditLogs.FirstOrDefaultAsync(x => x.Id == req.Id, ct);
        if (a is null) return null;

        return new AuditLogDetailDto(
            a.Id, a.CreatedAt,
            new AuditActorDto(a.ActorId, a.ActorName),
            a.Action.ToString(),
            new AuditTargetDto(a.TargetType, a.TargetId),
            a.IpAddress, a.RequestId, a.Reason,
            a.BeforeJson, a.AfterJson);
    }
}

// ── EXPORT (202) ─────────────────────────────────────────────────────────────────

public record ExportAuditLogQuery(string InitiatedByStaffId) : IRequest<ExportAuditLogResult>;
public record ExportAuditLogResult(string JobId, string StatusUrl);

public class ExportAuditLogHandler(IApplicationDbContext db, IJobDispatcher jobDispatcher)
    : IRequestHandler<ExportAuditLogQuery, ExportAuditLogResult>
{
    public async Task<ExportAuditLogResult> Handle(ExportAuditLogQuery req, CancellationToken ct)
    {
        var job = new Domain.Entities.BackgroundJob
        {
            Type                = "export",
            Status              = "queued",
            InitiatedByStaffId  = req.InitiatedByStaffId
        };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(ct);
        jobDispatcher.Enqueue(job.Id, job.Type);
        return new(job.Id, $"/api/v1/admin/jobs/{job.Id}");
    }
}
