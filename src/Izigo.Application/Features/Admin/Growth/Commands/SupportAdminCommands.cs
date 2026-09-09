using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Growth.Commands;

public record SupportCommandResult(bool Success, string? ErrorCode, object? Data = null);

// ── POST /admin/support/tickets/{id}/reply ────────────────────────────────────
// internal_note=true messages NEVER reach the customer (server-enforced).

public record ReplyToTicketCommand(string TicketId, string Body, string? AttachmentsJson,
    bool IsInternalNote, string StaffId, string StaffName) : IRequest<SupportCommandResult>;

public class ReplyToTicketHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<ReplyToTicketCommand, SupportCommandResult>
{
    public async Task<SupportCommandResult> Handle(ReplyToTicketCommand cmd, CancellationToken ct)
    {
        var ticket = await db.SupportTickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null) return new(false, "TICKET_NOT_FOUND");
        if (ticket.Status == TicketStatus.Closed) return new(false, "TICKET_CLOSED");

        var message = new SupportTicketMessage
        {
            TicketId        = cmd.TicketId,
            SenderId        = cmd.StaffId,
            SenderType      = "staff",
            Body            = cmd.Body,
            IsInternalNote  = cmd.IsInternalNote,
            AttachmentsJson = cmd.AttachmentsJson
        };
        db.SupportTicketMessages.Add(message);

        // Move ticket from open → pending when staff replies to customer
        if (!cmd.IsInternalNote && ticket.Status == TicketStatus.Open)
            ticket.Status = TicketStatus.Pending;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.SupportTicketAction,
            "SupportTicket", cmd.TicketId,
            reason: cmd.IsInternalNote ? "Internal note added" : "Staff replied", ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null, new { message_id = message.Id });
    }
}

// ── POST /admin/support/tickets/{id}/assign ───────────────────────────────────
// staff_id or "self".

public record AssignTicketCommand(string TicketId, string? StaffIdTarget,
    bool AssignToSelf, string StaffId, string StaffName) : IRequest<SupportCommandResult>;

public class AssignTicketHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<AssignTicketCommand, SupportCommandResult>
{
    public async Task<SupportCommandResult> Handle(AssignTicketCommand cmd, CancellationToken ct)
    {
        var ticket = await db.SupportTickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null) return new(false, "TICKET_NOT_FOUND");

        var targetStaffId = cmd.AssignToSelf ? cmd.StaffId : cmd.StaffIdTarget;
        if (string.IsNullOrWhiteSpace(targetStaffId)) return new(false, "STAFF_ID_REQUIRED");

        var before = new { ticket.AssignedStaffId };
        ticket.AssignedStaffId = targetStaffId;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.SupportTicketAction,
            "SupportTicket", cmd.TicketId, reason: $"Assigned to {targetStaffId}",
            before: before, after: new { AssignedStaffId = targetStaffId }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── PATCH /admin/support/tickets/{id} ────────────────────────────────────────
// Change status, priority, category, resolution_code.

public record UpdateTicketCommand(string TicketId, string? Status, string? Priority,
    string? Category, string? ResolutionCode, string StaffId, string StaffName)
    : IRequest<SupportCommandResult>;

public class UpdateTicketHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UpdateTicketCommand, SupportCommandResult>
{
    public async Task<SupportCommandResult> Handle(UpdateTicketCommand cmd, CancellationToken ct)
    {
        var ticket = await db.SupportTickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null) return new(false, "TICKET_NOT_FOUND");

        var before = new { ticket.Status, ticket.Priority, ticket.Category, ticket.ResolutionCode };

        if (!string.IsNullOrWhiteSpace(cmd.Status) &&
            Enum.TryParse<TicketStatus>(cmd.Status, true, out var parsedStatus))
            ticket.Status = parsedStatus;

        if (!string.IsNullOrWhiteSpace(cmd.Priority) &&
            Enum.TryParse<TicketPriority>(cmd.Priority, true, out var parsedPriority))
            ticket.Priority = parsedPriority;

        if (!string.IsNullOrWhiteSpace(cmd.Category))
            ticket.Category = cmd.Category;

        if (!string.IsNullOrWhiteSpace(cmd.ResolutionCode))
            ticket.ResolutionCode = cmd.ResolutionCode;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.SupportTicketAction,
            "SupportTicket", cmd.TicketId, reason: "Ticket updated",
            before: before, after: new { ticket.Status, ticket.Priority }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/support/tickets/{id}/resolve-with ─────────────────────────────
// Atomically executes the financial action AND closes the ticket in one transaction.
// action = refund | credit | waive; amount?, reason (required).

public record ResolveTicketWithCommand(string TicketId, string Action, long? Amount,
    string Reason, string StaffId, string StaffName) : IRequest<SupportCommandResult>;

public class ResolveTicketWithHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<ResolveTicketWithCommand, SupportCommandResult>
{
    private static readonly HashSet<string> ValidActions =
        new(StringComparer.OrdinalIgnoreCase) { "refund", "credit", "waive" };

    public async Task<SupportCommandResult> Handle(ResolveTicketWithCommand cmd, CancellationToken ct)
    {
        if (!ValidActions.Contains(cmd.Action))
            return new(false, "INVALID_ACTION");

        if (string.IsNullOrWhiteSpace(cmd.Reason))
            return new(false, "REASON_REQUIRED");

        var ticket = await db.SupportTickets.FirstOrDefaultAsync(t => t.Id == cmd.TicketId, ct);
        if (ticket is null) return new(false, "TICKET_NOT_FOUND");
        if (ticket.Status == TicketStatus.Resolved || ticket.Status == TicketStatus.Closed)
            return new(false, "TICKET_ALREADY_CLOSED");

        var before = new { ticket.Status };

        // Financial action: credit or refund the customer's wallet, or waive (no money movement)
        if (cmd.Action is "refund" or "credit" && cmd.Amount.HasValue && cmd.Amount.Value > 0)
        {
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == ticket.UserId, ct);
            if (wallet is not null)
            {
                wallet.Balance += cmd.Amount.Value;
                db.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId     = wallet.Id,
                    Type         = WalletTransactionType.Adjustment,
                    Amount       = cmd.Amount.Value,
                    BalanceAfter = wallet.Balance,
                    Title        = cmd.Action == "refund" ? "Support refund" : "Support credit",
                    Subtitle     = cmd.Reason,
                    ReferenceId  = ticket.Id,
                    Reason       = cmd.Reason,
                    CreatedBy    = cmd.StaffId
                });
            }
        }

        // Close ticket atomically
        ticket.Status         = TicketStatus.Resolved;
        ticket.ResolutionCode = cmd.Action;

        // Add a resolution note visible to both agent and (for non-internal notes) customer
        db.SupportTicketMessages.Add(new SupportTicketMessage
        {
            TicketId       = cmd.TicketId,
            SenderId       = cmd.StaffId,
            SenderType     = "staff",
            Body           = $"Ticket resolved via {cmd.Action}. {cmd.Reason}",
            IsInternalNote = false
        });

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.SupportTicketAction,
            "SupportTicket", cmd.TicketId, reason: $"Resolved with action: {cmd.Action}. {cmd.Reason}",
            before: before, after: new { Status = "resolved", Action = cmd.Action, Amount = cmd.Amount },
            ct: ct);

        // All of the above — financial write + ticket close — commit in one SaveChangesAsync
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}
