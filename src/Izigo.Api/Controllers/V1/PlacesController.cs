using Izigo.Application.Features.Places.Commands;
using Izigo.Application.Features.Places.Dtos;
using Izigo.Application.Features.Places.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1;

/// <summary>
/// Manages a rider's saved places (home, work, favourites) and their
/// recent location history.
/// </summary>
[Route("api/v1/places")]
[Authorize(Policy = "RiderPolicy")]
public class PlacesController : BaseController
{
    /// <summary>Returns all saved places for the authenticated rider.</summary>
    [HttpGet("")]
    public async Task<IActionResult> GetPlaces()
        => Ok(await Mediator.Send(new GetPlacesQuery(CurrentUserId)));

    /// <summary>Adds a new saved place.</summary>
    [HttpPost("")]
    public async Task<IActionResult> CreatePlace([FromBody] UpsertPlaceRequest body)
        => Ok(await Mediator.Send(new AddPlaceCommand(CurrentUserId, body)));

    /// <summary>Updates an existing saved place.</summary>
    [HttpPatch("{id}")]
    public async Task<IActionResult> UpdatePlace(string id, [FromBody] UpsertPlaceRequest body)
        => Ok(await Mediator.Send(new UpdatePlaceCommand(CurrentUserId, id, body)));

    /// <summary>Deletes a saved place by ID.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePlace(string id)
    {
        await Mediator.Send(new DeletePlaceCommand(CurrentUserId, id));
        return NoContent();
    }

    /// <summary>Returns the rider's most recently searched or visited locations.</summary>
    [HttpGet("recent")]
    public async Task<IActionResult> GetRecentPlaces([FromQuery] int limit = 5)
        => Ok(await Mediator.Send(new GetRecentPlacesQuery(CurrentUserId, limit)));
}
