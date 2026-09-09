using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Config.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1;

[Route("api/v1")]
public class ConfigController(ICurrentUserService currentUser) : BaseController
{
    // ── GET /config ───────────────────────────────────────────────────────────
    // Anonymous. Both apps call this on splash before anything else.

    [AllowAnonymous]
    [HttpGet("config")]
    public async Task<IActionResult> GetConfig(CancellationToken ct)
    {
        var market = currentUser.Market ?? "ci";
        var result = await Mediator.Send(new GetConfigQuery(market), ct);
        return Ok(result);
    }

    // ── GET /config/version-check ─────────────────────────────────────────────
    // Query: platform (android|ios), version (e.g. 1.2.0)
    // Returns: action (none|soft_update|force_update|maintenance), store_url

    [AllowAnonymous]
    [HttpGet("config/version-check")]
    public async Task<IActionResult> VersionCheck(
        [FromQuery] string platform,
        [FromQuery] string version,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(version))
            return Unprocessable("VALIDATION_ERROR", "platform and version query params are required.");

        var market = currentUser.Market ?? "ci";
        var result = await Mediator.Send(new GetVersionCheckQuery(platform, version, market), ct);
        return Ok(result);
    }

    // ── GET /zones ────────────────────────────────────────────────────────────
    // Geofence polygons + per-zone verticals_enabled.

    [AllowAnonymous]
    [HttpGet("zones")]
    public async Task<IActionResult> GetZones(CancellationToken ct)
    {
        var market = currentUser.Market ?? "ci";
        var result = await Mediator.Send(new GetZonesQuery(market), ct);
        return Ok(result);
    }

    // ── GET /pages/{slug} ─────────────────────────────────────────────────────
    // Slugs: terms | privacy | about | driver-terms | cancellation-policy

    [AllowAnonymous]
    [HttpGet("pages/{slug}")]
    public async Task<IActionResult> GetPage(string slug, CancellationToken ct)
    {
        var language = HttpContext.Request.Headers["Accept-Language"]
            .FirstOrDefault()?.Split(',')[0].Trim().Split('-')[0] ?? "fr";
        var market = currentUser.Market ?? "ci";

        var result = await Mediator.Send(new GetPageQuery(slug, language, market), ct);
        return Ok(result);
    }

    // ── GET /languages ────────────────────────────────────────────────────────

    [AllowAnonymous]
    [HttpGet("languages")]
    public async Task<IActionResult> GetLanguages(CancellationToken ct)
    {
        var result = await Mediator.Send(new GetLanguagesQuery(), ct);
        return Ok(result);
    }

    // ── GET /onboarding-slides ────────────────────────────────────────────────
    // Query: audience=rider|driver

    [AllowAnonymous]
    [HttpGet("onboarding-slides")]
    public async Task<IActionResult> GetOnboardingSlides(
        [FromQuery] string audience,
        CancellationToken ct)
    {
        if (!new[] { "rider", "driver" }.Contains(audience?.ToLower()))
            return Unprocessable("VALIDATION_ERROR", "audience must be 'rider' or 'driver'.");

        var language = HttpContext.Request.Headers["Accept-Language"]
            .FirstOrDefault()?.Split(',')[0].Trim().Split('-')[0] ?? "fr";
        var market = currentUser.Market ?? "ci";

        var result = await Mediator.Send(
            new GetOnboardingSlidesQuery(audience!.ToLower(), language, market), ct);
        return Ok(result);
    }

    // ── GET /banners ──────────────────────────────────────────────────────────
    // Rider only, authenticated. Query: placement=home|wallet (optional)

    [Authorize(Policy = "AppPolicy")]
    [HttpGet("banners")]
    public async Task<IActionResult> GetBanners(
        [FromQuery] string? placement,
        CancellationToken ct)
    {
        var market = currentUser.Market ?? "ci";
        var result = await Mediator.Send(new GetBannersQuery(market, placement), ct);
        return Ok(result);
    }

    // ── GET /service-classes ──────────────────────────────────────────────────
    // Static catalogue: zen_car, zen_bike, zen_coride, package_small, package_large

    [AllowAnonymous]
    [HttpGet("service-classes")]
    public async Task<IActionResult> GetServiceClasses(CancellationToken ct)
    {
        var result = await Mediator.Send(new GetServiceClassesQuery(), ct);
        return Ok(result);
    }
}
