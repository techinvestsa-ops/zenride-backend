namespace Izigo.Application.Features.Support.Dtos;

public record FaqDto(string Id, string Question, string Answer, string Category);

// spec: flat object, not a list
public record SupportChannelsDto(
    string CallNumber,
    string Whatsapp,
    string Email,
    string SosNumber,
    string Hours,
    bool LiveChatEnabled
);

public record TicketCategoryDto(string Code, string Label, string? Description);

public record SupportTicketDto(
    string Id,
    string Reference,
    string Category,
    string Description,
    string? TripId,
    string Status,
    string Priority,
    List<TicketMessageDto> Messages,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

public record TicketMessageDto(
    string Id,
    string SenderType,  // user | staff
    string Body,
    string[] Attachments,
    DateTime CreatedAt
);

public record TicketSummaryDto(
    string Id,
    string Reference,
    string Category,
    string Status,
    string Priority,
    string? LastReply,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

// Requests
public record CreateTicketRequest(
    string Category,
    string Description,
    string? TripId,
    string[]? AttachmentsJson
);

public record ReplyTicketRequest(string Body, string[]? Attachments);
