using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Izigo.Application.Common.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Izigo.Infrastructure.Services;

/// <summary>
/// Google Maps Platform proxy — all API keys stay server-side.
/// When <c>GoogleMaps:ApiKey</c> is absent the service logs a warning and falls back
/// to Haversine straight-line geometry for route/ETA (so fare calculation still works
/// in dev without credentials). Autocomplete and geocode return empty/null, which the
/// handlers already tolerate.
/// </summary>
public class GoogleMapsService(
    IHttpClientFactory http,
    IConfiguration config,
    ILogger<GoogleMapsService> logger) : IGeoService
{
    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    // Base URLs
    private const string MapsBase   = "https://maps.googleapis.com/maps/api";
    private const string RoutesBase = "https://routes.googleapis.com";

    private string? ApiKey => config["GoogleMaps:ApiKey"];

    // ── Autocomplete ──────────────────────────────────────────────────────────

    public async Task<GeoAutocompleteResult[]> AutocompleteAsync(
        string q, double lat, double lng,
        string? sessionToken, string? country,
        CancellationToken ct = default)
    {
        if (!HasKey()) return [];

        var url = new StringBuilder($"{MapsBase}/place/autocomplete/json")
            .Append($"?input={Uri.EscapeDataString(q)}")
            .Append($"&location={lat},{lng}")
            .Append("&radius=50000")
            .Append($"&key={ApiKey}");

        if (!string.IsNullOrWhiteSpace(sessionToken))
            url.Append($"&sessiontoken={sessionToken}");
        if (!string.IsNullOrWhiteSpace(country))
            url.Append($"&components=country:{country}");

        try
        {
            var client   = http.CreateClient("maps");
            using var doc = await GetJsonAsync(client, url.ToString(), ct);
            if (doc is null) return [];

            var status = doc.RootElement.GetProperty("status").GetString();
            if (status is not ("OK" or "ZERO_RESULTS"))
            {
                logger.LogWarning("[Maps] Autocomplete status={Status}", status);
                return [];
            }

            return [.. doc.RootElement.GetProperty("predictions").EnumerateArray()
                .Select(p =>
                {
                    var placeId  = p.GetProperty("place_id").GetString() ?? "";
                    var sf       = p.TryGetProperty("structured_formatting", out var sfv) ? sfv : default;
                    var primary  = sf.ValueKind != JsonValueKind.Undefined &&
                                   sf.TryGetProperty("main_text", out var mt)
                                   ? mt.GetString() ?? ""
                                   : p.GetProperty("description").GetString() ?? "";
                    var secondary = sf.ValueKind != JsonValueKind.Undefined &&
                                    sf.TryGetProperty("secondary_text", out var st)
                                    ? st.GetString() : null;
                    var distM    = p.TryGetProperty("distance_meters", out var dm)
                                   ? dm.GetInt32() : (int?)null;
                    return new GeoAutocompleteResult(placeId, primary, secondary ?? "", distM);
                })];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Maps] Autocomplete failed q={Q}", q);
            return [];
        }
    }

    // ── Place Detail ──────────────────────────────────────────────────────────

    public async Task<GeoPlaceDetail?> GetPlaceDetailAsync(
        string placeId, CancellationToken ct = default)
    {
        if (!HasKey()) return null;

        var url = $"{MapsBase}/place/details/json" +
                  $"?place_id={Uri.EscapeDataString(placeId)}" +
                  "&fields=name,formatted_address,geometry" +
                  $"&key={ApiKey}";

        try
        {
            var client   = http.CreateClient("maps");
            using var doc = await GetJsonAsync(client, url, ct);
            if (doc is null) return null;

            var status = doc.RootElement.GetProperty("status").GetString();
            if (status != "OK")
            {
                logger.LogWarning("[Maps] PlaceDetail status={Status}", status);
                return null;
            }

            var result   = doc.RootElement.GetProperty("result");
            var name     = result.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var address  = result.GetProperty("formatted_address").GetString() ?? "";
            var loc      = result.GetProperty("geometry").GetProperty("location");
            var lat      = loc.GetProperty("lat").GetDouble();
            var lng      = loc.GetProperty("lng").GetDouble();
            return new GeoPlaceDetail(name, address, lat, lng);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Maps] PlaceDetail failed placeId={Id}", placeId);
            return null;
        }
    }

    // ── Reverse Geocode ───────────────────────────────────────────────────────

    public async Task<GeoReverseResult?> ReverseGeocodeAsync(
        double lat, double lng, CancellationToken ct = default)
    {
        if (!HasKey()) return null;

        var url = $"{MapsBase}/geocode/json?latlng={lat},{lng}&key={ApiKey}";

        try
        {
            var client   = http.CreateClient("maps");
            using var doc = await GetJsonAsync(client, url, ct);
            if (doc is null) return null;

            var status = doc.RootElement.GetProperty("status").GetString();
            if (status is not ("OK" or "ZERO_RESULTS")) return null;

            var results = doc.RootElement.GetProperty("results");
            if (results.GetArrayLength() == 0) return null;

            var first   = results[0];
            var address = first.GetProperty("formatted_address").GetString() ?? "";
            var loc     = first.GetProperty("geometry").GetProperty("location");
            return new GeoReverseResult(address,
                loc.GetProperty("lat").GetDouble(),
                loc.GetProperty("lng").GetDouble());
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Maps] ReverseGeocode failed lat={Lat} lng={Lng}", lat, lng);
            return null;
        }
    }

    // ── Forward Geocode ───────────────────────────────────────────────────────

    public async Task<GeoGeocodeResult?> GeocodeAsync(
        string address, CancellationToken ct = default)
    {
        if (!HasKey()) return null;

        var url = $"{MapsBase}/geocode/json" +
                  $"?address={Uri.EscapeDataString(address)}" +
                  $"&key={ApiKey}";

        try
        {
            var client   = http.CreateClient("maps");
            using var doc = await GetJsonAsync(client, url, ct);
            if (doc is null) return null;

            var status = doc.RootElement.GetProperty("status").GetString();
            if (status is not ("OK" or "ZERO_RESULTS")) return null;

            var results = doc.RootElement.GetProperty("results");
            if (results.GetArrayLength() == 0) return null;

            var first    = results[0];
            var formatted = first.GetProperty("formatted_address").GetString() ?? "";
            var placeId  = first.TryGetProperty("place_id", out var pid) ? pid.GetString() : null;
            var loc      = first.GetProperty("geometry").GetProperty("location");
            return new GeoGeocodeResult(formatted,
                loc.GetProperty("lat").GetDouble(),
                loc.GetProperty("lng").GetDouble(),
                placeId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Maps] Geocode failed address={Address}", address);
            return null;
        }
    }

    // ── Route ─────────────────────────────────────────────────────────────────

    public async Task<GeoRouteResult?> GetRouteAsync(
        GeoPoint origin, GeoPoint destination,
        GeoPoint[]? waypoints, string mode,
        CancellationToken ct = default)
    {
        if (!HasKey())
            return HaversineRoute(origin, destination);

        // Routes API v2 uses DRIVE / TWO_WHEELER / WALK / BICYCLE
        var travelMode = mode.ToUpperInvariant() switch
        {
            "driving"   => "DRIVE",
            "walking"   => "WALK",
            "bicycling" => "BICYCLE",
            "transit"   => "TRANSIT",
            _           => "DRIVE"
        };

        var body = new
        {
            origin      = LatLngWaypoint(origin),
            destination = LatLngWaypoint(destination),
            intermediates = waypoints?.Select(LatLngWaypoint).ToArray() ?? [],
            travelMode,
            computeAlternativeRoutes = false,
            routingPreference        = "TRAFFIC_AWARE"
        };

        try
        {
            var client = http.CreateClient("maps");
            client.DefaultRequestHeaders.Remove("X-Goog-Api-Key");
            client.DefaultRequestHeaders.Add("X-Goog-Api-Key", ApiKey);
            client.DefaultRequestHeaders.Remove("X-Goog-FieldMask");
            client.DefaultRequestHeaders.Add("X-Goog-FieldMask",
                "routes.polyline.encodedPolyline,routes.distanceMeters,routes.duration,routes.staticDuration");

            var resp = await client.PostAsJsonAsync(
                $"{RoutesBase}/directions/v2:computeRoutes", body, _opts, ct);

            var raw = await resp.Content.ReadAsStringAsync(ct);
            logger.LogDebug("[Maps] Routes raw: {Raw}", raw);

            if (!resp.IsSuccessStatusCode)
            {
                logger.LogWarning("[Maps] Routes API {Status} — falling back to Haversine",
                    (int)resp.StatusCode);
                return HaversineRoute(origin, destination);
            }

            using var doc    = JsonDocument.Parse(raw);
            var routes       = doc.RootElement.GetProperty("routes");
            if (routes.GetArrayLength() == 0)
                return HaversineRoute(origin, destination);

            var route       = routes[0];
            var polyline    = route.GetProperty("polyline").GetProperty("encodedPolyline").GetString() ?? "";
            var distM       = route.GetProperty("distanceMeters").GetInt32();

            // duration comes back as a protobuf Duration string, e.g. "723s"
            var durStr      = route.TryGetProperty("duration", out var dur)
                              ? dur.GetString() : null;
            var durS        = ParseDurationSeconds(durStr);

            var staticDurStr = route.TryGetProperty("staticDuration", out var sd)
                               ? sd.GetString() : null;
            var staticDurS  = ParseDurationSeconds(staticDurStr);

            // If traffic-aware duration differs from static, it's the traffic-adjusted value
            var trafficDurS = durS != staticDurS ? durS : (int?)null;

            return new GeoRouteResult(polyline, distM, staticDurS ?? durS ?? 0, trafficDurS);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Maps] GetRoute failed — falling back to Haversine");
            return HaversineRoute(origin, destination);
        }
    }

    // ── ETA / Distance Matrix ─────────────────────────────────────────────────

    public async Task<GeoEtaResult[]> GetEtaAsync(
        GeoPoint origin, GeoPoint[] destinations, CancellationToken ct = default)
    {
        if (!HasKey() || destinations.Length == 0)
            return [.. destinations.Select(d => HaversineEta(origin, d))];

        var destStr = string.Join("|", destinations.Select(d => $"{d.Lat},{d.Lng}"));
        var url = $"{MapsBase}/distancematrix/json" +
                  $"?origins={origin.Lat},{origin.Lng}" +
                  $"&destinations={Uri.EscapeDataString(destStr)}" +
                  "&mode=driving" +
                  $"&key={ApiKey}";

        try
        {
            var client    = http.CreateClient("maps");
            using var doc = await GetJsonAsync(client, url, ct);
            if (doc is null)
                return [.. destinations.Select(d => HaversineEta(origin, d))];

            var status = doc.RootElement.GetProperty("status").GetString();
            if (status != "OK")
            {
                logger.LogWarning("[Maps] DistanceMatrix status={Status}", status);
                return [.. destinations.Select(d => HaversineEta(origin, d))];
            }

            var elements = doc.RootElement
                .GetProperty("rows")[0]
                .GetProperty("elements")
                .EnumerateArray()
                .ToArray();

            return [.. destinations.Select((d, i) =>
            {
                if (i >= elements.Length) return HaversineEta(origin, d);
                var el    = elements[i];
                var elSt  = el.GetProperty("status").GetString() ?? "UNKNOWN";
                if (elSt != "OK") return new GeoEtaResult(d.Lat, d.Lng, null, null, elSt);

                var durS  = el.GetProperty("duration").GetProperty("value").GetInt32();
                var distM = el.GetProperty("distance").GetProperty("value").GetInt32();
                return new GeoEtaResult(d.Lat, d.Lng, durS, distM, "OK");
            })];
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[Maps] GetEta failed");
            return [.. destinations.Select(d => HaversineEta(origin, d))];
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool HasKey()
    {
        if (!string.IsNullOrWhiteSpace(ApiKey)) return true;
        logger.LogWarning("[Maps:DEV] GoogleMaps:ApiKey not configured — using fallback geometry");
        return false;
    }

    private static async Task<JsonDocument?> GetJsonAsync(
        HttpClient client, string url, CancellationToken ct)
    {
        var resp = await client.GetAsync(url, ct);
        if (!resp.IsSuccessStatusCode) return null;
        var stream = await resp.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static object LatLngWaypoint(GeoPoint p) => new
    {
        location = new { latLng = new { latitude = p.Lat, longitude = p.Lng } }
    };

    // Parse protobuf Duration string "723s" → 723
    private static int? ParseDurationSeconds(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var trimmed = s.TrimEnd('s');
        return double.TryParse(trimmed, out var v) ? (int)v : null;
    }

    // Haversine straight-line distance in metres
    private static double Haversine(GeoPoint a, GeoPoint b)
    {
        const double R = 6_371_000;
        var phi1 = a.Lat * Math.PI / 180;
        var phi2 = b.Lat * Math.PI / 180;
        var dphi = (b.Lat - a.Lat) * Math.PI / 180;
        var dlam = (b.Lng - a.Lng) * Math.PI / 180;
        var x    = Math.Sin(dphi / 2) * Math.Sin(dphi / 2) +
                   Math.Cos(phi1) * Math.Cos(phi2) *
                   Math.Sin(dlam / 2) * Math.Sin(dlam / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(x), Math.Sqrt(1 - x));
    }

    // ~30 km/h average city speed for ETA estimation when Google is not available
    private const double FallbackSpeedMs = 8.33;

    private static GeoRouteResult HaversineRoute(GeoPoint origin, GeoPoint destination)
    {
        var distM  = (int)Haversine(origin, destination);
        var durS   = (int)(distM / FallbackSpeedMs);
        return new GeoRouteResult(string.Empty, distM, durS, null);
    }

    private static GeoEtaResult HaversineEta(GeoPoint origin, GeoPoint dest)
    {
        var distM = (int)Haversine(origin, dest);
        var durS  = (int)(distM / FallbackSpeedMs);
        return new GeoEtaResult(dest.Lat, dest.Lng, durS, distM, "OK");
    }
}
