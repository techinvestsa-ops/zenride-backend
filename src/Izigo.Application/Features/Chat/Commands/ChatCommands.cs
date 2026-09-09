using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Chat.Dtos;
using Izigo.Application.Features.Chat.Queries;
using Izigo.Application.Features.Realtime.Dtos;
using Izigo.Domain.Common;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Chat.Commands;

// ── POST /conversations/trip/{tripId} — get or create ────────────────────────

public record GetOrCreateTripConversationCommand(string UserId, string TripId)
    : IRequest<ConversationDto>;

public class GetOrCreateTripConversationHandler(IApplicationDbContext db)
    : IRequestHandler<GetOrCreateTripConversationCommand, ConversationDto>
{
    public async Task<ConversationDto> Handle(GetOrCreateTripConversationCommand cmd, CancellationToken ct)
    {
        var trip = await db.Trips
            .Include(t => t.Conversation)
            .FirstOrDefaultAsync(t => t.Id == cmd.TripId &&
                (t.RiderId == cmd.UserId || t.DriverId == cmd.UserId), ct)
            ?? throw new KeyNotFoundException("Trip not found or access denied.");

        if (trip.Conversation != null)
        {
            var existing = trip.Conversation;
            return new ConversationDto(existing.Id, existing.Kind.ToString().ToLower(),
                existing.TripId, trip.Code, Counterpart: null, 0, null,
                existing.IsClosed, existing.CreatedAt);
        }

        var conv = new Conversation
        {
            Kind   = ConversationKind.Trip,
            TripId = trip.Id,
        };
        db.Conversations.Add(conv);
        await db.SaveChangesAsync(ct);

        return new ConversationDto(conv.Id, "trip", trip.Id, trip.Code,
            Counterpart: null, 0, null, false, conv.CreatedAt);
    }
}

// ── POST /conversations/{id}/messages ────────────────────────────────────────

public record SendMessageCommand(string SenderId, string SenderRole, string ConversationId, SendMessageRequest Request)
    : IRequest<MessageDto>;

public class SendMessageHandler(IApplicationDbContext db, IRealtimeService realtime)
    : IRequestHandler<SendMessageCommand, MessageDto>
{
    public async Task<MessageDto> Handle(SendMessageCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;

        _ = await db.Conversations.FirstOrDefaultAsync(c => c.Id == cmd.ConversationId, ct)
            ?? throw new KeyNotFoundException("Conversation not found.");

        if (!Enum.TryParse<MessageType>(req.Type, true, out var msgType))
            throw new ArgumentException("VALIDATION_ERROR: Invalid message type.");

        // Deduplicate on LocalId (client-side idempotency)
        if (req.LocalId != null)
        {
            var dup = await db.Messages
                .FirstOrDefaultAsync(m => m.LocalId == req.LocalId &&
                                          m.ConversationId == cmd.ConversationId, ct);
            if (dup != null) return GetConversationsHandler.MapMessage(dup);
        }

        var message = new Message
        {
            ConversationId = cmd.ConversationId,
            SenderId       = cmd.SenderId,
            SenderRole     = cmd.SenderRole,
            Type           = msgType,
            Body           = req.Body,
            MediaUrl       = req.MediaUrl,
            DurationS      = req.DurationS,
            LocalId        = req.LocalId,
        };

        db.Messages.Add(message);
        await db.SaveChangesAsync(ct);

        var dto = GetConversationsHandler.MapMessage(message);

        // chat.message → presence-trip channel (both parties see it in real-time)
        // Also push to the recipient's private channel in case they're not in the trip room.
        var conv = await db.Conversations
            .FirstOrDefaultAsync(c => c.Id == cmd.ConversationId, ct);

        var msgEvent = new ChatMessageEvent(cmd.ConversationId, new ChatMessageInfo(
            dto.Id, dto.SenderId, dto.SenderRole, dto.Type, dto.Body, dto.MediaUrl,
            dto.DurationS, dto.LocalId, dto.IsRead, dto.SentAt));

        if (conv?.TripId != null)
        {
            await realtime.PublishToTripAsync(conv.TripId, "chat.message", msgEvent, ct);

            // Also notify the other party's private channel
            var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == conv.TripId, ct);
            if (trip != null)
            {
                var recipientId = cmd.SenderRole == "driver" ? trip.RiderId : trip.DriverId;
                if (recipientId != null)
                {
                    if (cmd.SenderRole == "driver")
                        await realtime.PublishToUserAsync(recipientId, "chat.message", msgEvent, ct);
                    else
                        await realtime.PublishToDriverAsync(recipientId, "chat.message", msgEvent, ct);
                }
            }
        }

        return dto;
    }
}

// ── POST /conversations/{id}/read ────────────────────────────────────────────

public record MarkConversationReadCommand(string UserId, string ConversationId) : IRequest;

public class MarkConversationReadHandler(IApplicationDbContext db)
    : IRequestHandler<MarkConversationReadCommand>
{
    public async Task Handle(MarkConversationReadCommand cmd, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        await db.Messages
            .Where(m => m.ConversationId == cmd.ConversationId &&
                        m.SenderId != cmd.UserId &&
                        m.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadAt, now), ct);
    }
}

// ── POST /calls/mask ──────────────────────────────────────────────────────────

public record InitiateMaskedCallCommand(string UserId, string TripId) : IRequest<MaskedCallDto>;

public class InitiateMaskedCallHandler(IApplicationDbContext db)
    : IRequestHandler<InitiateMaskedCallCommand, MaskedCallDto>
{
    public async Task<MaskedCallDto> Handle(InitiateMaskedCallCommand cmd, CancellationToken ct)
    {
        _ = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == cmd.TripId &&
                (t.RiderId == cmd.UserId || t.DriverId == cmd.UserId), ct)
            ?? throw new KeyNotFoundException("Trip not found or access denied.");

        // TODO: create a masked call session via Twilio/Africa's Talking proxy
        var token = Guid.CreateVersion7().ToString("N")[..12].ToUpper();

        return new MaskedCallDto(
            Token:         token,
            MaskedNumber:  "+225 XX XX XX XX",   // replaced by proxy number in production
            ExpiresInSec:  300);
    }
}
