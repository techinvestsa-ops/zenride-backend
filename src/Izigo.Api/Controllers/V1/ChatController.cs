using Izigo.Application.Features.Chat.Commands;
using Izigo.Application.Features.Chat.Dtos;
using Izigo.Application.Features.Chat.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1;

/// <summary>
/// Handles in-app messaging between riders and drivers: conversation listing,
/// message history, sending messages, and masked phone calls.
/// </summary>
[Route("api/v1/conversations")]
[Authorize(Policy = "AppPolicy")]
public class ChatController : BaseController
{
    private string UserRole =>
        HttpContext.Request.Headers["X-App"].FirstOrDefault() ?? "rider";

    /// <summary>Returns all conversations for the authenticated user.</summary>
    [HttpGet("")]
    public async Task<IActionResult> GetConversations()
        => Ok(await Mediator.Send(new GetConversationsQuery(CurrentUserId, UserRole)));

    /// <summary>Returns messages for a conversation. Use ?before={message_id} for cursor pagination.</summary>
    [HttpGet("{id}/messages")]
    public async Task<IActionResult> GetMessages(string id,
        [FromQuery] string? before = null, [FromQuery] int limit = 30)
        => Ok(await Mediator.Send(new GetMessagesQuery(CurrentUserId, id, before, limit)));

    /// <summary>Sends a new message in a conversation.</summary>
    [HttpPost("{id}/messages")]
    public async Task<IActionResult> SendMessage(string id, [FromBody] SendMessageRequest body)
        => Ok(await Mediator.Send(new SendMessageCommand(CurrentUserId, UserRole, id, body)));

    /// <summary>Marks all messages in a conversation as read by the current user.</summary>
    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkConversationRead(string id)
    {
        await Mediator.Send(new MarkConversationReadCommand(CurrentUserId, id));
        return NoContent();
    }

    /// <summary>Creates or retrieves the conversation associated with a trip.</summary>
    [HttpPost("trip/{tripId}")]
    public async Task<IActionResult> GetOrCreateTripConversation(string tripId)
        => Ok(await Mediator.Send(new GetOrCreateTripConversationCommand(CurrentUserId, tripId)));

    /// <summary>Initiates a masked (anonymised) phone call to the other party.</summary>
    [HttpPost("/api/v1/calls/mask")]
    public async Task<IActionResult> MaskCall([FromBody] MaskCallRequest body)
        => Ok(await Mediator.Send(new InitiateMaskedCallCommand(CurrentUserId, body.TripId)));

    public record MaskCallRequest(string TripId);
}
