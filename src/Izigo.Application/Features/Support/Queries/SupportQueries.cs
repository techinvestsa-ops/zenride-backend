using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Settings;
using Izigo.Application.Features.Support.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Izigo.Application.Features.Support.Queries;

// ── GET /support/faqs ─────────────────────────────────────────────────────────
// spec: ?audience=rider|driver (not ?category)

public record GetFaqsQuery(string? Audience) : IRequest<List<FaqDto>>;

public class GetFaqsHandler : IRequestHandler<GetFaqsQuery, List<FaqDto>>
{
    private static readonly List<FaqDto> RiderFaqs =
    [
        new("f1", "How do I cancel a ride?",         "Go to your active ride and tap 'Cancel'. A fee may apply if the driver has already arrived.", "rides"),
        new("f2", "How do I top up my wallet?",      "Go to Wallet → Top up. Choose your mobile money provider and enter the amount.",              "wallet"),
        new("f3", "My driver didn't arrive. What should I do?", "Tap 'Contact Driver' to call them. If they are unreachable, you can cancel without charge.", "rides"),
        new("f4", "How do I report a safety issue?", "During a trip, press the shield icon to trigger an SOS. After the trip, use 'Report Trip'.",  "safety"),
        new("f5", "What is the referral bonus?",     "You earn 1,000 XOF when someone signs up using your referral code and completes their first trip.", "referral"),
        new("f6", "How does the co-ride pricing work?", "Each seat is priced individually. The total is seat price × seats + platform fee.",          "coride"),
    ];

    private static readonly List<FaqDto> DriverFaqs =
    [
        new("d1", "When do I receive my earnings?",  "Wallet earnings are credited instantly. Withdrawals are processed within 1 business day.",   "earnings"),
        new("d2", "How do I update my vehicle?",     "Go to Driver → Vehicles, then edit or add a vehicle. New vehicles require admin approval.", "vehicles"),
        new("d3", "What is the cash settlement cap?", "When you owe more than 10,000 XOF from cash trips, you cannot go online until you settle.", "earnings"),
        new("d4", "How do I complete KYC?",          "Complete all 8 steps in the onboarding checklist, then tap Submit for Review.",              "kyc"),
    ];

    public Task<List<FaqDto>> Handle(GetFaqsQuery req, CancellationToken ct)
    {
        var result = req.Audience?.ToLower() switch
        {
            "driver" => DriverFaqs,
            "rider"  => RiderFaqs,
            _        => [.. RiderFaqs, .. DriverFaqs]
        };
        return Task.FromResult(result);
    }
}

// ── GET /support/channels ─────────────────────────────────────────────────────
// spec: flat object with call_number, whatsapp, email, sos_number, hours, live_chat_enabled

public record GetChannelsQuery : IRequest<SupportChannelsDto>;

public class GetChannelsHandler(IOptions<AppSettings> appOptions)
    : IRequestHandler<GetChannelsQuery, SupportChannelsDto>
{
    public Task<SupportChannelsDto> Handle(GetChannelsQuery req, CancellationToken ct)
    {
        var cfg = appOptions.Value;
        return Task.FromResult(new SupportChannelsDto(
            CallNumber:      cfg.SupportPhone,
            Whatsapp:        cfg.SupportWhatsapp,
            Email:           cfg.SupportEmail,
            SosNumber:       cfg.SosNumber,
            Hours:           cfg.SupportHours,
            LiveChatEnabled: cfg.LiveChatEnabled));
    }
}

// ── GET /support/categories ───────────────────────────────────────────────────

public record GetCategoriesQuery : IRequest<List<TicketCategoryDto>>;

public class GetCategoriesHandler : IRequestHandler<GetCategoriesQuery, List<TicketCategoryDto>>
{
    // Codes match the spec enum: earnings_payouts | trip_customer_issue | technical | account | lost_item | safety
    private static readonly List<TicketCategoryDto> Categories =
    [
        new("earnings_payouts",      "Earnings & Payouts",        "Charge, refund, wallet, or driver payout issue"),
        new("trip_customer_issue",   "Trip / Customer Issue",      "Problem with a ride, delivery, or co-ride"),
        new("technical",             "Technical Issue",            "App crash, login, or platform error"),
        new("account",               "Account",                    "Profile, KYC, or verification problem"),
        new("lost_item",             "Lost Item",                  "Item left in or taken from a vehicle"),
        new("safety",                "Safety Concern",             "Safety incident or emergency report"),
    ];

    public Task<List<TicketCategoryDto>> Handle(GetCategoriesQuery req, CancellationToken ct)
        => Task.FromResult(Categories);
}

// ── GET /support/tickets ──────────────────────────────────────────────────────

public record GetTicketsQuery(string UserId, string? Status, int Page, int PerPage)
    : IRequest<(List<TicketSummaryDto> Items, int Total)>;

public class GetTicketsHandler(IApplicationDbContext db)
    : IRequestHandler<GetTicketsQuery, (List<TicketSummaryDto>, int)>
{
    public async Task<(List<TicketSummaryDto>, int)> Handle(GetTicketsQuery req, CancellationToken ct)
    {
        var q = db.SupportTickets.Where(t => t.UserId == req.UserId);

        if (!string.IsNullOrEmpty(req.Status) &&
            Enum.TryParse<Domain.Enums.TicketStatus>(req.Status, true, out var status))
            q = q.Where(t => t.Status == status);

        var total = await q.CountAsync(ct);
        var tickets = await q
            .OrderByDescending(t => t.CreatedAt)
            .Skip((req.Page - 1) * req.PerPage)
            .Take(req.PerPage)
            .ToListAsync(ct);

        var ticketIds = tickets.Select(t => t.Id).ToList();
        var lastMessages = await db.SupportTicketMessages
            .Where(m => ticketIds.Contains(m.TicketId))
            .GroupBy(m => m.TicketId)
            .Select(g => new { TicketId = g.Key, LastBody = g.OrderByDescending(m => m.CreatedAt).First().Body })
            .ToDictionaryAsync(x => x.TicketId, x => x.LastBody, ct);

        var items = tickets.Select(t => new TicketSummaryDto(
            t.Id, t.Reference, t.Category,
            t.Status.ToString().ToLower(), t.Priority.ToString().ToLower(),
            lastMessages.GetValueOrDefault(t.Id),
            t.CreatedAt, t.UpdatedAt)).ToList();

        return (items, total);
    }
}

// ── GET /support/tickets/{id} ─────────────────────────────────────────────────

public record GetTicketQuery(string UserId, string TicketId) : IRequest<SupportTicketDto>;

public class GetTicketHandler(IApplicationDbContext db)
    : IRequestHandler<GetTicketQuery, SupportTicketDto>
{
    public async Task<SupportTicketDto> Handle(GetTicketQuery req, CancellationToken ct)
    {
        var ticket = await db.SupportTickets
            .FirstOrDefaultAsync(t => t.Id == req.TicketId && t.UserId == req.UserId, ct)
            ?? throw new KeyNotFoundException("Ticket not found.");

        var rawMessages = await db.SupportTicketMessages
            .Where(m => m.TicketId == req.TicketId && !m.IsInternalNote)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(ct);

        var messages = rawMessages.Select(m => new TicketMessageDto(
            m.Id, m.SenderType, m.Body,
            System.Text.Json.JsonSerializer.Deserialize<string[]>(m.AttachmentsJson ?? "[]") ?? [],
            m.CreatedAt)).ToList();

        return new SupportTicketDto(
            ticket.Id, ticket.Reference, ticket.Category, ticket.Description,
            ticket.TripId, ticket.Status.ToString().ToLower(), ticket.Priority.ToString().ToLower(),
            messages, ticket.CreatedAt, ticket.UpdatedAt);
    }
}
