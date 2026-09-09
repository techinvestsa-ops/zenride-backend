namespace Izigo.Application.Features.Chat.Dtos;

public record ConversationDto(
    string Id,
    string Kind,            // trip | support
    string? TripId,
    string? TripCode,
    CounterpartDto? Counterpart,    // spec: counterpart (name, photo, role)
    int UnreadCount,
    MessageDto? LastMessage,
    bool IsClosed,
    DateTime CreatedAt
);

public record CounterpartDto(
    string Name,
    string? PhotoUrl,
    string Role            // rider | driver | support
);

public record MessageDto(
    string Id,
    string SenderId,
    string SenderRole,      // rider | driver | support | system
    string Type,            // text | image | audio | system
    string? Body,
    string? MediaUrl,
    int? DurationS,
    string? LocalId,
    bool IsRead,
    DateTime SentAt        // spec uses sent_at, not created_at
);

public record MaskedCallDto(
    string Token,
    string MaskedNumber,
    int ExpiresInSec
);

// Requests
public record SendMessageRequest(
    string Type,
    string? Body,
    string? MediaUrl,
    int? DurationS,
    string? LocalId
);
