using Izigo.Application.Features.Quotes.Commands;
using Izigo.Application.Features.Quotes.Dtos;
using Izigo.Application.Features.Quotes.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1;

/// <summary>
/// Handles ride fare quotation requests and retrieval of active fare rules
/// before a ride is booked.
/// </summary>
[Route("api/v1")]
[Authorize(Policy = "RiderPolicy")]
public class QuotesController : BaseController
{
    /// <summary>Generates one or more fare quotes for the supplied trip parameters.</summary>
    [HttpPost("quotes")]
    public async Task<IActionResult> CreateQuote([FromBody] CreateQuoteRequest body)
        => Ok(await Mediator.Send(new CreateQuoteCommand(CurrentUserId, body)));

    /// <summary>Returns a previously generated quote by its ID.</summary>
    [HttpGet("quotes/{quoteId}")]
    public async Task<IActionResult> GetQuote(string quoteId)
    {
        var result = await Mediator.Send(new GetQuoteQuery(CurrentUserId, quoteId));
        if (result == null) return Fail("QUOTE_EXPIRED", "This quote has expired. Please request a new one.", 410);
        return Ok(result);
    }

    /// <summary>Returns the active fare rules for the rider's zone and service classes.</summary>
    [HttpGet("fare-rules")]
    public async Task<IActionResult> GetFareRules()
        => Ok(await Mediator.Send(new GetFareRulesQuery("ci")));  // market from config/header when multi-market
}
