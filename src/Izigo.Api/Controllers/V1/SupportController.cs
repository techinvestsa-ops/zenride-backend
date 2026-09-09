using Izigo.Application.Common.Models;
using Izigo.Application.Features.Support.Commands;
using Izigo.Application.Features.Support.Dtos;
using Izigo.Application.Features.Support.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1;

/// <summary>
/// Provides the customer support experience: FAQs, contact channels, ticket
/// creation and management, and ticket replies.
/// </summary>
[Route("api/v1/support")]
[Authorize(Policy = "AppPolicy")]
public class SupportController : BaseController
{
    private string UserRole =>
        HttpContext.Request.Headers["X-App"].FirstOrDefault() ?? "rider";

    /// <summary>Returns a list of frequently asked questions.</summary>
    [HttpGet("faqs")]
    public async Task<IActionResult> GetFaqs([FromQuery] string? audience)
        => Ok(await Mediator.Send(new GetFaqsQuery(audience)));

    /// <summary>Returns available support contact channels (chat, phone, email).</summary>
    [HttpGet("channels")]
    public async Task<IActionResult> GetChannels()
        => Ok(await Mediator.Send(new GetChannelsQuery()));

    /// <summary>Returns the available support ticket categories.</summary>
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories()
        => Ok(await Mediator.Send(new GetCategoriesQuery()));

    /// <summary>Opens a new support ticket.</summary>
    [HttpPost("tickets")]
    public async Task<IActionResult> CreateTicket([FromBody] CreateTicketRequest body)
        => Ok(await Mediator.Send(new CreateTicketCommand(CurrentUserId, UserRole, body)));

    /// <summary>Returns all support tickets raised by the authenticated user.</summary>
    [HttpGet("tickets")]
    public async Task<IActionResult> GetTickets(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int per_page = 20)
    {
        var (items, total) = await Mediator.Send(
            new GetTicketsQuery(CurrentUserId, status, page, per_page));
        return Ok(items, PagedMeta.From(page, per_page, total));
    }

    /// <summary>Returns the details and message thread of a specific ticket.</summary>
    [HttpGet("tickets/{id}")]
    public async Task<IActionResult> GetTicket(string id)
        => Ok(await Mediator.Send(new GetTicketQuery(CurrentUserId, id)));

    /// <summary>Adds a reply to an existing support ticket.</summary>
    [HttpPost("tickets/{id}/reply")]
    public async Task<IActionResult> ReplyToTicket(string id, [FromBody] ReplyTicketRequest body)
        => Ok(await Mediator.Send(new ReplyTicketCommand(CurrentUserId, id, body)));

    /// <summary>Closes an open support ticket.</summary>
    [HttpPost("tickets/{id}/close")]
    public async Task<IActionResult> CloseTicket(string id)
    {
        await Mediator.Send(new CloseTicketCommand(CurrentUserId, id));
        return NoContent();
    }
}
