using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Support.Dtos;
using Izigo.Application.Features.Support.Queries;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Support.Commands;

// ── POST /support/tickets ─────────────────────────────────────────────────────

public record CreateTicketCommand(string UserId, string UserRole, CreateTicketRequest Request)
    : IRequest<SupportTicketDto>;

public class CreateTicketHandler(IApplicationDbContext db)
    : IRequestHandler<CreateTicketCommand, SupportTicketDto>
{
    public async Task<SupportTicketDto> Handle(CreateTicketCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;

        var ticket = new SupportTicket
        {
            UserId      = cmd.UserId,
            UserRole    = cmd.UserRole,
            Category    = req.Category,
            Description = req.Description,
            TripId      = req.TripId,
            Status      = TicketStatus.Open,
            Priority    = TicketPriority.Medium,
            Reference   = $"TKT-{Guid.CreateVersion7():N}"[..12].ToUpper(),
            Market      = "ci",
        };

        db.SupportTickets.Add(ticket);
        await db.SaveChangesAsync(ct);

        // Seed with the user's opening message
        db.SupportTicketMessages.Add(new SupportTicketMessage
        {
            TicketId       = ticket.Id,
            SenderId       = cmd.UserId,
            SenderType     = "user",
            Body           = req.Description,
            AttachmentsJson = System.Text.Json.JsonSerializer.Serialize(req.AttachmentsJson ?? []),
        });
        await db.SaveChangesAsync(ct);

        return await new GetTicketHandler(db)
            .Handle(new GetTicketQuery(cmd.UserId, ticket.Id), ct);
    }
}

// ── POST /support/tickets/{id}/reply ─────────────────────────────────────────

public record ReplyTicketCommand(string UserId, string TicketId, ReplyTicketRequest Request)
    : IRequest<TicketMessageDto>;

public class ReplyTicketHandler(IApplicationDbContext db)
    : IRequestHandler<ReplyTicketCommand, TicketMessageDto>
{
    public async Task<TicketMessageDto> Handle(ReplyTicketCommand cmd, CancellationToken ct)
    {
        var ticket = await db.SupportTickets
            .FirstOrDefaultAsync(t => t.Id == cmd.TicketId && t.UserId == cmd.UserId, ct)
            ?? throw new KeyNotFoundException("Ticket not found.");

        if (ticket.Status == TicketStatus.Closed)
            throw new InvalidOperationException("CONFLICT: Cannot reply to a closed ticket.");

        if (ticket.Status == TicketStatus.Resolved)
            ticket.Status = TicketStatus.Open;   // reopen on user reply

        var msg = new SupportTicketMessage
        {
            TicketId        = ticket.Id,
            SenderId        = cmd.UserId,
            SenderType      = "user",
            Body            = cmd.Request.Body,
            AttachmentsJson = System.Text.Json.JsonSerializer.Serialize(cmd.Request.Attachments ?? []),
        };

        db.SupportTicketMessages.Add(msg);
        await db.SaveChangesAsync(ct);

        return new TicketMessageDto(msg.Id, "user", msg.Body,
            cmd.Request.Attachments ?? [], msg.CreatedAt);
    }
}

// ── POST /support/tickets/{id}/close ─────────────────────────────────────────

public record CloseTicketCommand(string UserId, string TicketId) : IRequest;

public class CloseTicketHandler(IApplicationDbContext db)
    : IRequestHandler<CloseTicketCommand>
{
    public async Task Handle(CloseTicketCommand cmd, CancellationToken ct)
    {
        var ticket = await db.SupportTickets
            .FirstOrDefaultAsync(t => t.Id == cmd.TicketId && t.UserId == cmd.UserId, ct)
            ?? throw new KeyNotFoundException("Ticket not found.");

        if (ticket.Status == TicketStatus.Closed)
            return;  // idempotent

        ticket.Status = TicketStatus.Closed;
        await db.SaveChangesAsync(ct);
    }
}
