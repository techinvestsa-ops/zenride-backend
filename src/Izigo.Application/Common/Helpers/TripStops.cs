using System.Text.Json;
using System.Text.Json.Serialization;

namespace Izigo.Application.Common.Helpers;

/// <summary>An intermediate stop between pickup and drop-off.</summary>
public record TripStop(
    string Label,
    double Lat,
    double Lng,
    string? PlaceId = null,
    DateTime? ArrivedAt = null);

/// <summary>
/// Stops are stored as JSON on the quote and the trip (StopsJson), in the
/// order the driver visits them. A ride may have up to <see cref="Max"/>.
/// </summary>
public static class TripStops
{
    public const int Max = 2;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static List<TripStop> Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<TripStop>>(json, Json) ?? []; }
        catch (JsonException) { return []; }
    }

    public static string Write(IEnumerable<TripStop> stops) => JsonSerializer.Serialize(stops.ToList(), Json);

    /// <summary>The next stop the driver has not reached yet, if any.</summary>
    public static (int Index, TripStop Stop)? Next(IReadOnlyList<TripStop> stops)
    {
        for (var i = 0; i < stops.Count; i++)
            if (stops[i].ArrivedAt is null) return (i, stops[i]);
        return null;
    }
}
