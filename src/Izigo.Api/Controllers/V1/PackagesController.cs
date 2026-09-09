using Izigo.Application.Common.Models;
using Izigo.Application.Features.Packages.Commands;
using Izigo.Application.Features.Packages.Dtos;
using Izigo.Application.Features.Packages.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1;

/// <summary>
/// Manages package / delivery orders: creation, history, live tracking,
/// cancellation, and post-delivery rating.
/// </summary>
[Route("api/v1/packages")]
public class PackagesController : BaseController
{
    /// <summary>Creates a new package delivery order.</summary>
    [Authorize(Policy = "RiderPolicy")]
    [HttpPost("")]
    public async Task<IActionResult> CreatePackage(
        [FromBody] CreatePackageRequest body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey)
        => Ok(await Mediator.Send(new CreatePackageCommand(CurrentUserId, body, idempotencyKey)));

    /// <summary>Returns a paginated list of the rider's package orders.</summary>
    [Authorize(Policy = "RiderPolicy")]
    [HttpGet("")]
    public async Task<IActionResult> GetPackages(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int per_page = 20)
    {
        var (items, total) = await Mediator.Send(
            new GetPackagesQuery(CurrentUserId, status, page, per_page));
        return Ok(items, PagedMeta.From(page, per_page, total));
    }

    /// <summary>Returns details of a specific package order.</summary>
    [Authorize(Policy = "RiderPolicy")]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetPackage(string id)
        => Ok(await Mediator.Send(new GetPackageQuery(CurrentUserId, id)));

    /// <summary>Returns live tracking information for a package using its public tracking ID.</summary>
    [AllowAnonymous]
    [HttpGet("track/{trackingId}")]
    public async Task<IActionResult> TrackPackage(string trackingId)
        => Ok(await Mediator.Send(new TrackPackageQuery(trackingId)));

    /// <summary>Cancels an active package delivery order.</summary>
    [Authorize(Policy = "RiderPolicy")]
    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> CancelPackage(string id, [FromBody] CancelPackageRequest? body)
    {
        await Mediator.Send(new CancelPackageCommand(CurrentUserId, id, body?.Reason));
        return NoContent();
    }

    /// <summary>Submits a rating for a completed delivery.</summary>
    [Authorize(Policy = "RiderPolicy")]
    [HttpPost("{id}/rate")]
    public async Task<IActionResult> RatePackage(string id, [FromBody] RatePackageRequest body)
    {
        await Mediator.Send(new RatePackageCommand(CurrentUserId, id, body));
        return NoContent();
    }
}
