using System.Collections.Concurrent;

namespace Izigo.Application.Features.Admin.Realtime;

/// <summary>Limits admin hub driver.location publishes (mirrors rider trip-room throttle).</summary>
public static class AdminLocationThrottle
{
    private static readonly ConcurrentDictionary<string, DateTime> LastByTrip = new();
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(4);

    public static bool Allow(string tripId)
    {
        var now = DateTime.UtcNow;
        if (LastByTrip.TryGetValue(tripId, out var last) && now - last < Interval)
            return false;
        LastByTrip[tripId] = now;
        return true;
    }
}
