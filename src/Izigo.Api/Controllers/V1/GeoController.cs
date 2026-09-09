using Izigo.Application.Features.Geo.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1;

[Route("api/v1/geo")]
[Authorize(Policy = "AppPolicy")]
public class GeoController : BaseController
{
    // ── GET /geo/autocomplete ─────────────────────────────────────────────────
    // Query: q, lat, lng, session_token?, country?
    // Returns: [{ place_id, primary_text, secondary_text, distance_m }]

    [HttpGet("autocomplete")]
    public async Task<IActionResult> Autocomplete(
        [FromQuery] string q,
        [FromQuery] double lat,
        [FromQuery] double lng,
        [FromQuery(Name = "session_token")] string? sessionToken,
        [FromQuery] string? country,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q))
            return Unprocessable("VALIDATION_ERROR", "q is required.");

        var result = await Mediator.Send(
            new AutocompleteQuery(q, lat, lng, sessionToken, country), ct);
        return Ok(result);
    }

    // ── GET /geo/place/{placeId} ──────────────────────────────────────────────
    // Returns: { name, formatted_address, lat, lng }

    [HttpGet("place/{placeId}")]
    public async Task<IActionResult> GetPlace(string placeId, CancellationToken ct)
    {
        var result = await Mediator.Send(new GetPlaceDetailQuery(placeId), ct);
        return Ok(result);
    }

    // ── GET /geo/reverse-geocode ──────────────────────────────────────────────
    // Query: lat, lng

    [HttpGet("reverse-geocode")]
    public async Task<IActionResult> ReverseGeocode(
        [FromQuery] double lat,
        [FromQuery] double lng,
        CancellationToken ct)
    {
        var result = await Mediator.Send(new ReverseGeocodeQuery(lat, lng), ct);
        return Ok(result);
    }

    // ── GET /geo/geocode ──────────────────────────────────────────────────────
    // Query: address

    [HttpGet("geocode")]
    public async Task<IActionResult> Geocode(
        [FromQuery] string address,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(address))
            return Unprocessable("VALIDATION_ERROR", "address is required.");

        var result = await Mediator.Send(new GeocodeQuery(address), ct);
        return Ok(result);
    }

    // ── GET /geo/route ────────────────────────────────────────────────────────
    // Query: origin (lat,lng), destination (lat,lng), waypoints?, mode=driving|bike
    // Returns: { encoded_polyline, distance_m, duration_s, duration_in_traffic_s }

    [HttpGet("route")]
    public async Task<IActionResult> GetRoute(
        [FromQuery(Name = "origin_lat")] double originLat,
        [FromQuery(Name = "origin_lng")] double originLng,
        [FromQuery(Name = "dest_lat")] double destLat,
        [FromQuery(Name = "dest_lng")] double destLng,
        [FromQuery] string[]? waypoints,
        [FromQuery] string mode = "driving",
        CancellationToken ct = default)
    {
        if (!new[] { "driving", "bike" }.Contains(mode.ToLower()))
            return Unprocessable("VALIDATION_ERROR", "mode must be 'driving' or 'bike'.");

        var result = await Mediator.Send(
            new GetRouteQuery(originLat, originLng, destLat, destLng, waypoints, mode), ct);
        return Ok(result);
    }

    // ── GET /geo/eta ──────────────────────────────────────────────────────────
    // Query: origin_lat, origin_lng, destinations (pipe-separated "lat,lng" pairs)
    // Returns: [{ dest_lat, dest_lng, duration_s, distance_m, status }]

    [HttpGet("eta")]
    public async Task<IActionResult> GetEta(
        [FromQuery(Name = "origin_lat")] double originLat,
        [FromQuery(Name = "origin_lng")] double originLng,
        [FromQuery] string destinations,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(destinations))
            return Unprocessable("VALIDATION_ERROR", "destinations is required.");

        var result = await Mediator.Send(
            new GetEtaQuery(originLat, originLng, destinations), ct);
        return Ok(result);
    }

    // ── GET /geo/nearby-drivers ───────────────────────────────────────────────
    // Rider only. Query: lat, lng, radius_m, vertical
    // Returns fuzzed positions — NO driver identity exposed (contract requirement)

    [Authorize(Policy = "RiderPolicy")]
    [HttpGet("nearby-drivers")]
    public async Task<IActionResult> GetNearbyDrivers(
        [FromQuery] double lat,
        [FromQuery] double lng,
        [FromQuery(Name = "radius_m")] int radiusM = 3000,
        [FromQuery] string? vertical = null,
        CancellationToken ct = default)
    {
        var result = await Mediator.Send(
            new GetNearbyDriversQuery(lat, lng, radiusM, vertical), ct);
        return Ok(result);
    }
}
