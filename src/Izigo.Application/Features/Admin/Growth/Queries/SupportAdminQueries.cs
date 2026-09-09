using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Growth.Queries;

// ── A16: SUPPORT DESK ────────────────────────────────────────────────────────

// ── GET /admin/support/tickets ────────────────────────────────────────────────
// sla_minutes_left is negative when the SLA deadline is already breached.

public record TicketRowDto(string Id, string Reference, string UserRole, string Category,
    string Status, string Priority, string? AssignedStaffId, double? SlaMinutesLeft,
    DateTime CreatedAt, string Market);

public record GetAdminTicketsQuery(string Market, string? Status, string? Category,
    string? Priority, string? Assignee, string? Audience, bool? PastSla,
    int Page, int PerPage) : IRequest<object>;

public class GetAdminTicketsHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminTicketsQuery, object>
{
    public async Task<object> Handle(GetAdminTicketsQuery req, CancellationToken ct)
    {
        var query = db.SupportTickets.Where(t => t.Market == req.Market).AsQueryable();

        if (!string.IsNullOrWhiteSpace(req.Status) &&
            Enum.TryParse<TicketStatus>(req.Status, true, out var parsedStatus))
            query = query.Where(t => t.Status == parsedStatus);

        if (!string.IsNullOrWhiteSpace(req.Category))
            query = query.Where(t => t.Category == req.Category);

        if (!string.IsNullOrWhiteSpace(req.Priority) &&
            Enum.TryParse<TicketPriority>(req.Priority, true, out var parsedPriority))
            query = query.Where(t => t.Priority == parsedPriority);

        if (!string.IsNullOrWhiteSpace(req.Assignee))
            query = query.Where(t => t.AssignedStaffId == req.Assignee);

        // audience=rider|driver maps to UserRole field
        if (!string.IsNullOrWhiteSpace(req.Audience))
            query = query.Where(t => t.UserRole == req.Audience.ToLower());

        // past_sla: SlaDeadline has passed and ticket is still open
        if (req.PastSla == true)
            query = query.Where(t => t.SlaDeadline != null &&
                                     t.SlaDeadline < DateTime.UtcNow &&
                                     t.Status != TicketStatus.Resolved &&
                                     t.Status != TicketStatus.Closed);

        var perPage  = Math.Max(1, Math.Min(100, req.PerPage));
        var total    = await query.CountAsync(ct);
        var lastPage = (int)Math.Ceiling((double)total / perPage);

        var now     = DateTime.UtcNow;
        var tickets = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((req.Page - 1) * perPage).Take(perPage)
            .Select(t => new
            {
                t.Id, t.Reference, t.UserRole, t.Category, t.Status, t.Priority,
                t.AssignedStaffId, t.SlaDeadline, t.CreatedAt, t.Market
            })
            .ToListAsync(ct);

        var rows = tickets.Select(t =>
        {
            double? slaMinutesLeft = t.SlaDeadline.HasValue
                ? (t.SlaDeadline.Value - now).TotalMinutes
                : null;
            return new TicketRowDto(
                t.Id, t.Reference, t.UserRole, t.Category,
                t.Status.ToString().ToLower(), t.Priority.ToString().ToLower(),
                t.AssignedStaffId, slaMinutesLeft is not null ? Math.Round(slaMinutesLeft.Value, 1) : null,
                t.CreatedAt, t.Market);
        }).ToArray();

        var facets = new Dictionary<string, Dictionary<string, int>>
        {
            ["status"]   = tickets.GroupBy(t => t.Status.ToString().ToLower())
                               .ToDictionary(g => g.Key, g => g.Count()),
            ["priority"] = tickets.GroupBy(t => t.Priority.ToString().ToLower())
                               .ToDictionary(g => g.Key, g => g.Count()),
            ["audience"] = tickets.GroupBy(t => t.UserRole)
                               .ToDictionary(g => g.Key, g => g.Count())
        };

        return AdminApiResponse.Ok(rows, new AdminPagedMeta(req.Page, perPage, total, lastPage, facets));
    }
}

// ── GET /admin/support/tickets/{id} ──────────────────────────────────────────
// Thread: messages, attachments, linked trip, customer's recent history.

public record TicketMessageDto(string Id, string SenderId, string SenderType, string Body,
    bool IsInternalNote, string? AttachmentsJson, DateTime SentAt);

public record TicketDetailDto(
    TicketRowDto Ticket, string? UserId, string? TripId,
    IEnumerable<TicketMessageDto> Messages,
    IEnumerable<object> RecentTrips);

public record GetAdminTicketDetailQuery(string TicketId) : IRequest<TicketDetailDto?>;

public class GetAdminTicketDetailHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminTicketDetailQuery, TicketDetailDto?>
{
    public async Task<TicketDetailDto?> Handle(GetAdminTicketDetailQuery req, CancellationToken ct)
    {
        var ticket = await db.SupportTickets
            .Where(t => t.Id == req.TicketId)
            .Select(t => new
            {
                t.Id, t.Reference, t.UserRole, t.Category, t.Status, t.Priority,
                t.AssignedStaffId, t.SlaDeadline, t.CreatedAt, t.Market,
                t.UserId, t.TripId, t.Description, t.ResolutionCode
            })
            .FirstOrDefaultAsync(ct);

        if (ticket is null) return null;

        var now            = DateTime.UtcNow;
        var slaMinutesLeft = ticket.SlaDeadline.HasValue
            ? (double?)Math.Round((ticket.SlaDeadline.Value - now).TotalMinutes, 1)
            : null;

        var row = new TicketRowDto(
            ticket.Id, ticket.Reference, ticket.UserRole, ticket.Category,
            ticket.Status.ToString().ToLower(), ticket.Priority.ToString().ToLower(),
            ticket.AssignedStaffId, slaMinutesLeft, ticket.CreatedAt, ticket.Market);

        var messages = await db.SupportTicketMessages
            .Where(m => m.TicketId == req.TicketId)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new TicketMessageDto(
                m.Id, m.SenderId, m.SenderType, m.Body,
                m.IsInternalNote, m.AttachmentsJson, m.CreatedAt))
            .ToListAsync(ct);

        // Customer's 5 most recent trips for context
        var recentTrips = await db.Trips
            .Where(t => t.RiderId == ticket.UserId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(5)
            .Select(t => (object)new
            {
                id          = t.Id,
                code        = t.Code,
                job_state   = t.JobState.ToString().ToLower(),
                fare        = t.FareGross,
                currency    = t.Currency,
                created_at  = t.CreatedAt
            })
            .ToListAsync(ct);

        return new TicketDetailDto(row, ticket.UserId, ticket.TripId, messages, recentTrips);
    }
}

// ── GET /admin/support/metrics ────────────────────────────────────────────────
// Open count, SLA breaches, median resolution, CSAT, per-agent throughput.

public record GetSupportMetricsQuery(string Market) : IRequest<object>;

public class GetSupportMetricsHandler(IApplicationDbContext db)
    : IRequestHandler<GetSupportMetricsQuery, object>
{
    public async Task<object> Handle(GetSupportMetricsQuery req, CancellationToken ct)
    {
        var now     = DateTime.UtcNow;
        var tickets = await db.SupportTickets
            .Where(t => t.Market == req.Market)
            .Select(t => new
            {
                t.Status, t.SlaDeadline, t.AssignedStaffId,
                t.CreatedAt, t.Priority
            })
            .ToListAsync(ct);

        var openCount   = tickets.Count(t => t.Status == TicketStatus.Open ||
                                             t.Status == TicketStatus.Pending);
        var slaBreaches = tickets.Count(t => t.SlaDeadline.HasValue &&
                                             t.SlaDeadline.Value < now &&
                                             t.Status != TicketStatus.Resolved &&
                                             t.Status != TicketStatus.Closed);

        var resolvedTickets = await db.SupportTickets
            .Where(t => t.Market == req.Market && t.Status == TicketStatus.Resolved)
            .Select(t => new { t.CreatedAt, t.UpdatedAt })
            .ToListAsync(ct);

        var resolutionTimes = resolvedTickets
            .Select(t => (t.UpdatedAt - t.CreatedAt).TotalMinutes)
            .OrderBy(x => x)
            .ToList();

        var medianResolutionMinutes = resolutionTimes.Count > 0
            ? Math.Round(resolutionTimes[resolutionTimes.Count / 2], 1)
            : 0d;

        var perAgent = tickets
            .Where(t => t.AssignedStaffId != null)
            .GroupBy(t => t.AssignedStaffId!)
            .Select(g => new { staff_id = g.Key, count = g.Count() })
            .Cast<object>().ToList();

        return new
        {
            success = true,
            data = new
            {
                open_count               = openCount,
                sla_breaches             = slaBreaches,
                median_resolution_minutes = medianResolutionMinutes,
                csat_score               = (object?)null,
                per_agent_throughput     = perAgent
            }
        };
    }
}

// ── GET /admin/support/canned-replies ────────────────────────────────────────
// Library of reply templates to keep agent answers consistent.

public record GetCannedRepliesQuery(string Market) : IRequest<object>;

public class GetCannedRepliesHandler : IRequestHandler<GetCannedRepliesQuery, object>
{
    private static readonly object[] Templates =
    [
        new { key = "trip_refund_issued",       title = "Refund Issued",
              body = "Your refund has been processed and will reflect in your wallet within a few minutes." },
        new { key = "trip_cancelled_no_driver",  title = "Trip Cancelled — No Driver",
              body = "We could not find a driver for your trip. You have not been charged. Please try again." },
        new { key = "driver_eta_delay",          title = "Driver Delay Apology",
              body = "We apologise for the delay. Your driver is on the way and should arrive shortly." },
        new { key = "kyc_documents_required",    title = "KYC Documents Required",
              body = "Your account requires document verification. Please submit the required documents in the app." },
        new { key = "account_suspended_notice",  title = "Account Suspension Notice",
              body = "Your account has been temporarily suspended. Please contact support for more information." },
        new { key = "wallet_adjustment_done",    title = "Wallet Adjustment Completed",
              body = "An adjustment has been made to your wallet balance. Please check your transaction history." },
        new { key = "package_lost_claim",        title = "Package Lost — Claim Opened",
              body = "We have opened a claim for your lost package and our team will follow up within 24 hours." },
        new { key = "general_apology",           title = "General Apology",
              body = "We are sorry for the inconvenience. Our team is looking into this and will resolve it shortly." }
    ];

    public Task<object> Handle(GetCannedRepliesQuery req, CancellationToken ct)
        => Task.FromResult<object>(new { success = true, data = Templates });
}
