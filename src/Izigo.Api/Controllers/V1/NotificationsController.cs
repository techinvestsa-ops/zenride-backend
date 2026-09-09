using Izigo.Application.Common.Models;
using Izigo.Application.Features.Notifications.Commands;
using Izigo.Application.Features.Notifications.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1;

/// <summary>
/// Manages the user's in-app notification feed: listing, unread counts,
/// marking as read, and deletion.
/// </summary>
[Route("api/v1/notifications")]
[Authorize(Policy = "AppPolicy")]
public class NotificationsController : BaseController
{
    /// <summary>Returns a paginated list of notifications for the authenticated user.</summary>
    [HttpGet("")]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] string? group,
        [FromQuery] int page = 1,
        [FromQuery] int per_page = 20)
    {
        var (items, total) = await Mediator.Send(
            new GetNotificationsQuery(CurrentUserId, group, page, per_page));
        return Ok(items, PagedMeta.From(page, per_page, total));
    }

    /// <summary>Returns the total count of unread notifications.</summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount()
        => Ok(await Mediator.Send(new GetUnreadCountQuery(CurrentUserId)));

    /// <summary>Marks a single notification as read.</summary>
    [HttpPost("{id}/read")]
    public async Task<IActionResult> MarkAsRead(string id)
    {
        await Mediator.Send(new MarkNotificationReadCommand(CurrentUserId, id));
        return NoContent();
    }

    /// <summary>Marks all notifications for the user as read.</summary>
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        await Mediator.Send(new MarkAllReadCommand(CurrentUserId));
        return NoContent();
    }

    /// <summary>Deletes a notification permanently.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteNotification(string id)
    {
        await Mediator.Send(new DeleteNotificationCommand(CurrentUserId, id));
        return NoContent();
    }
}
