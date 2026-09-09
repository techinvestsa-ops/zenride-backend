using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Chat.Dtos;
using Izigo.Application.Features.Rides.Helpers;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Chat.Queries;

// ── GET /conversations ────────────────────────────────────────────────────────

public record GetConversationsQuery(string UserId, string UserRole) : IRequest<List<ConversationDto>>;

public class GetConversationsHandler(IApplicationDbContext db)
    : IRequestHandler<GetConversationsQuery, List<ConversationDto>>
{
    public async Task<List<ConversationDto>> Handle(GetConversationsQuery req, CancellationToken ct)
    {
        List<string> tripIds;
        Dictionary<string, (string riderId, string? driverId, string code)> tripMap;

        if (req.UserRole == "rider")
        {
            var trips = await db.Trips
                .Where(t => t.RiderId == req.UserId && t.Conversation != null)
                .Select(t => new { t.Id, t.RiderId, t.DriverId, t.Code })
                .ToListAsync(ct);
            tripIds  = trips.Select(t => t.Id).ToList();
            tripMap  = trips.ToDictionary(t => t.Id, t => (t.RiderId, t.DriverId, t.Code));
        }
        else
        {
            var trips = await db.Trips
                .Where(t => t.DriverId == req.UserId && t.Conversation != null)
                .Select(t => new { t.Id, t.RiderId, t.DriverId, t.Code })
                .ToListAsync(ct);
            tripIds  = trips.Select(t => t.Id).ToList();
            tripMap  = trips.ToDictionary(t => t.Id, t => (t.RiderId, t.DriverId, t.Code));
        }

        var conversations = await db.Conversations
            .Include(c => c.Messages.OrderByDescending(m => m.CreatedAt).Take(1))
            .Where(c => c.TripId != null && tripIds.Contains(c.TripId))
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        // Load counterpart user info
        var counterpartIds = tripMap.Values
            .Select(t => req.UserRole == "rider" ? t.driverId : t.riderId)
            .Where(id => id != null)
            .Distinct()
            .ToList()!;

        var counterpartUsers = await db.Users
            .Where(u => counterpartIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        return conversations.Select(c =>
        {
            var last  = c.Messages.FirstOrDefault();
            var unread = last != null && last.SenderId != req.UserId && last.ReadAt == null ? 1 : 0;

            CounterpartDto? counterpart = null;
            if (c.TripId != null && tripMap.TryGetValue(c.TripId, out var tripInfo))
            {
                var counterpartId = req.UserRole == "rider" ? tripInfo.driverId : tripInfo.riderId;
                if (counterpartId != null && counterpartUsers.TryGetValue(counterpartId, out var cp))
                {
                    counterpart = new CounterpartDto(
                        cp.FullName,
                        cp.PhotoUrl,
                        req.UserRole == "rider" ? "driver" : "rider");
                }
            }

            return new ConversationDto(
                Id:          c.Id,
                Kind:        c.Kind.ToString().ToLower(),
                TripId:      c.TripId,
                TripCode:    c.TripId != null && tripMap.TryGetValue(c.TripId, out var t2) ? t2.code : null,
                Counterpart: counterpart,
                UnreadCount: unread,
                LastMessage: last == null ? null : MapMessage(last),
                IsClosed:    c.IsClosed,
                CreatedAt:   c.CreatedAt);
        }).ToList();
    }

    internal static MessageDto MapMessage(Domain.Entities.Message m) =>
        new(m.Id, m.SenderId, m.SenderRole, m.Type.ToString().ToLower(),
            m.Body, m.MediaUrl, m.DurationS, m.LocalId,
            m.ReadAt.HasValue,
            SentAt: m.CreatedAt);   // stored as CreatedAt, exposed as sent_at per spec
}

// ── GET /conversations/{id}/messages — cursor pagination ──────────────────────

public record GetMessagesQuery(string UserId, string ConversationId, string? Before, int Limit)
    : IRequest<List<MessageDto>>;

public class GetMessagesHandler(IApplicationDbContext db)
    : IRequestHandler<GetMessagesQuery, List<MessageDto>>
{
    public async Task<List<MessageDto>> Handle(GetMessagesQuery req, CancellationToken ct)
    {
        _ = await db.Conversations
            .FirstOrDefaultAsync(c => c.Id == req.ConversationId, ct)
            ?? throw new KeyNotFoundException("Conversation not found.");

        var q = db.Messages.Where(m => m.ConversationId == req.ConversationId);

        // Cursor: messages older than the supplied message id
        if (req.Before != null)
        {
            var cursorMsg = await db.Messages
                .FirstOrDefaultAsync(m => m.Id == req.Before, ct);
            if (cursorMsg != null)
                q = q.Where(m => m.CreatedAt < cursorMsg.CreatedAt);
        }

        return await q
            .OrderByDescending(m => m.CreatedAt)
            .Take(req.Limit)
            .Select(m => GetConversationsHandler.MapMessage(m))
            .ToListAsync(ct);
    }
}
