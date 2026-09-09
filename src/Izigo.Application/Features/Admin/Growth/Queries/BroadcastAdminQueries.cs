using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Growth.Queries;

// ── A17: BROADCASTS & REPORTS ─────────────────────────────────────────────────

// ── GET /admin/broadcasts ─────────────────────────────────────────────────────
// With delivered, opened_pct, failed, cost for SMS.

public record BroadcastRowDto(string Id, string Audience, string Channel,
    string Title, string Body, DateTime? ScheduledAt, DateTime? SentAt,
    int DeliveredCount, int FailedCount, decimal OpenedPct,
    bool IsCancelled, string? SentByStaffId, string Market);

public record GetAdminBroadcastsQuery(string Market, string? Channel, string? Status,
    int Page, int PerPage) : IRequest<object>;

public class GetAdminBroadcastsHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminBroadcastsQuery, object>
{
    public async Task<object> Handle(GetAdminBroadcastsQuery req, CancellationToken ct)
    {
        var query = db.Broadcasts.Where(b => b.Market == req.Market).AsQueryable();

        if (!string.IsNullOrWhiteSpace(req.Channel))
            query = query.Where(b => b.Channel == req.Channel);

        if (req.Status == "sent")      query = query.Where(b => b.SentAt != null && !b.IsCancelled);
        if (req.Status == "scheduled") query = query.Where(b => b.SentAt == null && !b.IsCancelled && b.ScheduledAt > DateTime.UtcNow);
        if (req.Status == "cancelled") query = query.Where(b => b.IsCancelled);

        var perPage  = Math.Max(1, Math.Min(100, req.PerPage));
        var total    = await query.CountAsync(ct);
        var lastPage = (int)Math.Ceiling((double)total / perPage);

        var broadcasts = await query
            .OrderByDescending(b => b.CreatedAt)
            .Skip((req.Page - 1) * perPage).Take(perPage)
            .ToListAsync(ct);

        var rows = broadcasts.Select(b => new BroadcastRowDto(
            b.Id, b.Audience, b.Channel, b.Title, b.Body,
            b.ScheduledAt, b.SentAt, b.DeliveredCount, b.FailedCount,
            b.OpenedPct, b.IsCancelled, b.SentByStaffId, b.Market)).ToArray();

        var facets = new Dictionary<string, Dictionary<string, int>>
        {
            ["channel"] = broadcasts.GroupBy(b => b.Channel)
                              .ToDictionary(g => g.Key, g => g.Count()),
            ["status"]  = new()
            {
                ["sent"]      = broadcasts.Count(b => b.SentAt != null && !b.IsCancelled),
                ["scheduled"] = broadcasts.Count(b => b.SentAt == null && !b.IsCancelled),
                ["cancelled"] = broadcasts.Count(b => b.IsCancelled)
            }
        };

        return AdminApiResponse.Ok(rows, new AdminPagedMeta(req.Page, perPage, total, lastPage, facets));
    }
}

// ── GET /admin/reports ────────────────────────────────────────────────────────
// Report library: key, contents, schedule, formats, last run.

public record GetAdminReportsQuery(string Market) : IRequest<object>;

public class GetAdminReportsHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminReportsQuery, object>
{
    private static readonly (string Key, string Contents, string[] Formats)[] Catalogue =
    [
        ("daily_trips",        "Trip summary by vertical, zone, and driver",           ["csv", "pdf"]),
        ("weekly_revenue",     "Revenue, commission, and driver earnings breakdown",    ["csv"]),
        ("driver_performance", "Acceptance rate, completion rate, ratings per driver", ["csv"]),
        ("kyc_queue",          "KYC applications by status, step, and reviewer",       ["csv"]),
        ("payout_summary",     "Payout approvals, failures, and cash settlements",     ["csv"]),
        ("coupon_performance", "Redemptions, cost, and new rider acquisition",         ["csv"]),
        ("support_desk",       "Ticket volume, SLA compliance, CSAT scores",           ["csv", "pdf"]),
        ("reconciliation",     "Gateway transactions vs platform records",             ["csv"])
    ];

    public async Task<object> Handle(GetAdminReportsQuery req, CancellationToken ct)
    {
        // Last run for each report key — query BackgroundJobs
        var jobsByKey = await db.BackgroundJobs
            .Where(j => j.Type.StartsWith("report_"))
            .OrderByDescending(j => j.CreatedAt)
            .Select(j => new { j.Type, j.Status, j.ResultUrl, j.CompletedAt })
            .ToListAsync(ct);

        var lastRunByKey = jobsByKey
            .GroupBy(j => j.Type.Replace("report_", ""))
            .ToDictionary(g => g.Key, g => g.First());

        var data = Catalogue.Select(r =>
        {
            lastRunByKey.TryGetValue(r.Key, out var lastRun);
            return (object)new
            {
                key        = r.Key,
                contents   = r.Contents,
                formats    = r.Formats,
                last_run   = lastRun?.CompletedAt,
                last_status = lastRun?.Status,
                result_url = lastRun?.ResultUrl
            };
        }).ToList();

        return new { success = true, data };
    }
}

// ── GET /admin/jobs/{id} ──────────────────────────────────────────────────────
// Shared by every 202 in the contract: status, progress, result_url, error.

public record GetBackgroundJobQuery(string JobId) : IRequest<object?>;

public class GetBackgroundJobHandler(IApplicationDbContext db)
    : IRequestHandler<GetBackgroundJobQuery, object?>
{
    public async Task<object?> Handle(GetBackgroundJobQuery req, CancellationToken ct)
    {
        var job = await db.BackgroundJobs
            .Where(j => j.Id == req.JobId)
            .Select(j => new
            {
                j.Id, j.Type, j.Status, j.Progress,
                j.ResultUrl, j.Error, j.CompletedAt, j.CreatedAt
            })
            .FirstOrDefaultAsync(ct);

        if (job is null) return null;

        return new
        {
            success = true,
            data = new
            {
                id           = job.Id,
                type         = job.Type,
                status       = job.Status,
                progress     = job.Progress,
                result_url   = job.ResultUrl,
                error        = job.Error,
                completed_at = job.CompletedAt,
                created_at   = job.CreatedAt
            }
        };
    }
}
