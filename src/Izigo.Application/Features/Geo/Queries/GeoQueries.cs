using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Settings;
using Izigo.Application.Features.Geo.Dtos;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Izigo.Application.Features.Geo.Queries;

// ── GET /geo/autocomplete ──────────────────────────────────────────────────────

public record AutocompleteQuery(
    string Q,
    double Lat,
    double Lng,
    string? SessionToken,
    string? Country
) : IRequest<AutocompleteResultDto[]>;

public class AutocompleteHandler(IGeoService geo)
    : IRequestHandler<AutocompleteQuery, AutocompleteResultDto[]>
{
    public async Task<AutocompleteResultDto[]> Handle(AutocompleteQuery req, CancellationToken ct)
    {
        var results = await geo.AutocompleteAsync(
            req.Q, req.Lat, req.Lng, req.SessionToken, req.Country, ct);

        return results.Select(r => new AutocompleteResultDto(
            r.PlaceId, r.PrimaryText, r.SecondaryText, r.DistanceM)).ToArray();
    }
}

// ── GET /geo/place/{place_id} ─────────────────────────────────────────────────

public record GetPlaceDetailQuery(string PlaceId) : IRequest<PlaceDetailDto>;

public class GetPlaceDetailHandler(IGeoService geo)
    : IRequestHandler<GetPlaceDetailQuery, PlaceDetailDto>
{
    public async Task<PlaceDetailDto> Handle(GetPlaceDetailQuery req, CancellationToken ct)
    {
        var result = await geo.GetPlaceDetailAsync(req.PlaceId, ct)
            ?? throw new KeyNotFoundException($"Place '{req.PlaceId}' not found.");

        return new PlaceDetailDto(
            result.Name, result.FormattedAddress, result.Lat, result.Lng);
    }
}

// ── GET /geo/reverse-geocode ──────────────────────────────────────────────────

public record ReverseGeocodeQuery(double Lat, double Lng) : IRequest<ReverseGeocodeDto>;

public class ReverseGeocodeHandler(IGeoService geo)
    : IRequestHandler<ReverseGeocodeQuery, ReverseGeocodeDto>
{
    public async Task<ReverseGeocodeDto> Handle(ReverseGeocodeQuery req, CancellationToken ct)
    {
        var result = await geo.ReverseGeocodeAsync(req.Lat, req.Lng, ct)
            ?? throw new KeyNotFoundException("Could not reverse-geocode the given coordinates.");

        return new ReverseGeocodeDto(result.FormattedAddress, result.Lat, result.Lng);
    }
}

// ── GET /geo/geocode ──────────────────────────────────────────────────────────

public record GeocodeQuery(string Address) : IRequest<GeocodeDto>;

public class GeocodeHandler(IGeoService geo) : IRequestHandler<GeocodeQuery, GeocodeDto>
{
    public async Task<GeocodeDto> Handle(GeocodeQuery req, CancellationToken ct)
    {
        var result = await geo.GeocodeAsync(req.Address, ct)
            ?? throw new KeyNotFoundException("Address could not be geocoded.");

        return new GeocodeDto(
            result.FormattedAddress, result.Lat, result.Lng, result.PlaceId);
    }
}

// ── GET /geo/route ────────────────────────────────────────────────────────────

public record GetRouteQuery(
    double OriginLat,
    double OriginLng,
    double DestLat,
    double DestLng,
    string[]? Waypoints,   // "lat,lng" pairs
    string Mode            // driving | bike
) : IRequest<RouteDto>;

public class GetRouteHandler(IGeoService geo) : IRequestHandler<GetRouteQuery, RouteDto>
{
    public async Task<RouteDto> Handle(GetRouteQuery req, CancellationToken ct)
    {
        var waypoints = req.Waypoints?
            .Select(w =>
            {
                var parts = w.Split(',');
                return parts.Length == 2 &&
                       double.TryParse(parts[0], out var lat) &&
                       double.TryParse(parts[1], out var lng)
                    ? new GeoPoint(lat, lng)
                    : null;
            })
            .Where(p => p != null)
            .Cast<GeoPoint>()
            .ToArray();

        var result = await geo.GetRouteAsync(
            new GeoPoint(req.OriginLat, req.OriginLng),
            new GeoPoint(req.DestLat, req.DestLng),
            waypoints,
            req.Mode,
            ct)
            ?? throw new InvalidOperationException("CONFLICT: Route not found between the given points.");

        return new RouteDto(
            result.EncodedPolyline, result.DistanceM,
            result.DurationS, result.DurationInTrafficS);
    }
}

// ── GET /geo/eta ──────────────────────────────────────────────────────────────

public record GetEtaQuery(
    double OriginLat,
    double OriginLng,
    string Destinations    // pipe-separated "lat,lng" pairs e.g. "5.36,-3.99|5.32,-4.02"
) : IRequest<EtaResultDto[]>;

public class GetEtaHandler(IGeoService geo) : IRequestHandler<GetEtaQuery, EtaResultDto[]>
{
    public async Task<EtaResultDto[]> Handle(GetEtaQuery req, CancellationToken ct)
    {
        var destinations = req.Destinations
            .Split('|', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair =>
            {
                var parts = pair.Split(',');
                return parts.Length == 2 &&
                       double.TryParse(parts[0].Trim(), out var lat) &&
                       double.TryParse(parts[1].Trim(), out var lng)
                    ? new GeoPoint(lat, lng)
                    : null;
            })
            .Where(p => p != null)
            .Cast<GeoPoint>()
            .ToArray();

        if (destinations.Length == 0)
            throw new ArgumentException("VALIDATION_ERROR: No valid destinations provided.");

        var results = await geo.GetEtaAsync(
            new GeoPoint(req.OriginLat, req.OriginLng), destinations, ct);

        return results.Select(r => new EtaResultDto(
            r.DestLat, r.DestLng, r.DurationS, r.DistanceM, r.Status)).ToArray();
    }
}

// ── GET /geo/nearby-drivers ───────────────────────────────────────────────────

public record GetNearbyDriversQuery(
    double Lat,
    double Lng,
    int RadiusM,
    string? Vertical
) : IRequest<NearbyDriverDto[]>;

public class GetNearbyDriversHandler(IApplicationDbContext db, IOptions<OpsSettings> opsOptions)
    : IRequestHandler<GetNearbyDriversQuery, NearbyDriverDto[]>
{
    private static readonly Random Rng = new();

    public async Task<NearbyDriverDto[]> Handle(GetNearbyDriversQuery req, CancellationToken ct)
    {
        // Convert radius to rough degree bounds for a DB pre-filter
        var degreeApprox = req.RadiusM / 111_000.0;

        var query = db.DriverProfiles
            .Where(d => d.IsOnline &&
                        d.LastLat != null && d.LastLng != null &&
                        d.LastLocationAt >= DateTime.UtcNow.AddMinutes(-opsOptions.Value.DriverLocationFreshnessMinutes) &&
                        (double)d.LastLat!.Value >= req.Lat - degreeApprox &&
                        (double)d.LastLat.Value <= req.Lat + degreeApprox &&
                        (double)d.LastLng!.Value >= req.Lng - degreeApprox &&
                        (double)d.LastLng.Value <= req.Lng + degreeApprox);

        if (!string.IsNullOrEmpty(req.Vertical) &&
            Enum.TryParse<Vertical>(req.Vertical, true, out var vertical))
        {
            query = query.Where(d => d.VerticalsAllowed.Contains(vertical));
        }

        var drivers = await query
            .Include(d => d.Vehicles.Where(v => v.IsActive))
            .Take(20)   // cap for performance; home map doesn't need more than ~20 dots
            .Select(d => new
            {
                d.LastLat, d.LastLng,
                VehicleType = d.Vehicles
                    .Where(v => v.IsActive)
                    .Select(v => v.Type.ToString().ToLower())
                    .FirstOrDefault() ?? "car"
            })
            .ToListAsync(ct);

        // Apply fuzz: add random noise so exact driver positions are never revealed
        return drivers.Select(d => new NearbyDriverDto(
            Lat: (double)d.LastLat!.Value + (Rng.NextDouble() - 0.5) * opsOptions.Value.NearbyDriverFuzzDegrees,
            Lng: (double)d.LastLng!.Value + (Rng.NextDouble() - 0.5) * opsOptions.Value.NearbyDriverFuzzDegrees,
            VehicleType: d.VehicleType
        )).ToArray();
    }
}
