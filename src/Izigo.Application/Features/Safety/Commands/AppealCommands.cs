using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Safety.Helpers;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Safety.Commands;

public record AppealMessageDto(string Id, string Sender, string Body, DateTime CreatedAt);

public record AppealDto(
    bool Locked,
    string? Reason,
    int ReportCount,
    string? TicketId,
    IReadOnlyList<AppealMessageDto> Messages);

public record GetAppealQuery(string UserId) : IRequest<AppealDto>;

public class GetAppealHandler(IApplicationDbContext db) : IRequestHandler<GetAppealQuery, AppealDto>
{
    public async Task<AppealDto> Handle(GetAppealQuery req, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == req.UserId, ct)
            ?? throw new KeyNotFoundException("User not found.");

        var count = await db.AccountReports.CountAsync(r => r.ReportedUserId == user.Id, ct);
        var locked = user.Status == UserStatus.Suspended;

        var ticket = await db.SupportTickets
            .Where(t => t.UserId == user.Id && t.Category == "appeal")
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (ticket is null)
            return new AppealDto(locked, user.SuspensionReason, count, null, []);

        var messages = await db.SupportTicketMessages
            .Where(m => m.TicketId == ticket.Id && !m.IsInternalNote)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new AppealMessageDto(
                m.Id,
                m.SenderType == "staff" ? "admin" : "you",
                m.Body,
                m.CreatedAt))
            .ToListAsync(ct);

        return new AppealDto(locked, user.SuspensionReason, count, ticket.Id, messages);
    }
}

public record PostAppealCommand(string UserId, string Body) : IRequest<AppealDto>;

public class PostAppealHandler(
    IApplicationDbContext db,
    IRealtimeService realtime,
    IEmailService email) : IRequestHandler<PostAppealCommand, AppealDto>
{
    public async Task<AppealDto> Handle(PostAppealCommand cmd, CancellationToken ct)
    {
        var body = cmd.Body?.Trim() ?? "";
        if (body.Length < 5)
            throw new ArgumentException("VALIDATION_ERROR: Write a short appeal so support can review it.");

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == cmd.UserId, ct)
            ?? throw new KeyNotFoundException("User not found.");
        if (user.Status != UserStatus.Suspended)
            throw new InvalidOperationException("CONFLICT: Only a suspended account can send an appeal.");

        var ticket = await db.SupportTickets
            .Where(t => t.UserId == user.Id &&
                        t.Category == "appeal" &&
                        t.Status != TicketStatus.Closed &&
                        t.Status != TicketStatus.Resolved)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

        if (ticket is null)
        {
            await AccountModeration.LockAccountAsync(
                db, user, user.SuspensionReason ?? "Account suspended.", ct);
            await db.SaveChangesAsync(ct);
            ticket = await db.SupportTickets
                .Where(t => t.UserId == user.Id && t.Category == "appeal")
                .OrderByDescending(t => t.CreatedAt)
                .FirstAsync(ct);
        }

        if (ticket.Status == TicketStatus.Pending)
            ticket.Status = TicketStatus.Open;

        db.SupportTicketMessages.Add(new Domain.Entities.SupportTicketMessage
        {
            TicketId = ticket.Id,
            SenderId = user.Id,
            SenderType = "user",
            Body = body
        });
        await db.SaveChangesAsync(ct);

        await AccountModeration.NotifyUserReplyAsync(realtime, email, ticket, user, body, ct);
        return await new GetAppealHandler(db).Handle(new GetAppealQuery(user.Id), ct);
    }
}
