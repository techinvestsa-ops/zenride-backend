namespace Izigo.Application.Features.Notifications.Dtos;

public record NotificationDto(
    string Id,
    string Type,
    string Title,
    string Body,
    string? ImageUrl,
    string? DeepLink,
    bool IsRead,
    string Group,
    DateTime CreatedAt
);

public record UnreadCountDto(int Count);
