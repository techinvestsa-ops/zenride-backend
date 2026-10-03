using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Realtime.Dtos;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;

namespace Izigo.Application.Features.Admin.Realtime;

/// <summary>Pushes admin-console SignalR events to market groups (admin-{market}).</summary>
public static class AdminRealtimeNotify
{
    public static string NormalizeMarket(string? market) =>
        string.IsNullOrWhiteSpace(market) ? "ci" : market.Trim().ToLowerInvariant();

    public static Task JobStateChangedAsync(IRealtimeService realtime, Trip trip, CancellationToken ct = default)
    {
        var market = NormalizeMarket(trip.Market);
        return SafePublishAsync(realtime, market, "job.state_changed", new
        {
            trip_id    = trip.Id,
            code       = trip.Code,
            vertical   = VerticalSlug(trip.Vertical),
            state      = trip.JobState.ToString().ToLowerInvariant(),
            rider_id   = trip.RiderId,
            driver_id  = trip.DriverId,
            market,
            changed_at = DateTime.UtcNow,
        }, ct);
    }

    public static Task JobCompletedAsync(IRealtimeService realtime, Trip trip, CancellationToken ct = default)
    {
        var market = NormalizeMarket(trip.Market);
        return SafePublishAsync(realtime, market, "job.completed", new
        {
            trip_id  = trip.Id,
            code     = trip.Code,
            vertical = VerticalSlug(trip.Vertical),
            market,
            at       = DateTime.UtcNow,
        }, ct);
    }

    public static Task DriverPresenceChangedAsync(
        IRealtimeService realtime, string? market, string driverId, bool online, CancellationToken ct = default)
    {
        var m = NormalizeMarket(market);
        return SafePublishAsync(realtime, m, "driver.presence_changed", new
        {
            driver_id  = driverId,
            online,
            market     = m,
            changed_at = DateTime.UtcNow,
        }, ct);
    }

    public static Task ConsoleInvalidateAsync(
        IRealtimeService realtime, string? market, string? scope = null, CancellationToken ct = default)
    {
        var m = NormalizeMarket(market);
        return SafePublishAsync(realtime, m, "console.invalidate", new
        {
            scope = scope ?? "all",
            market = m,
            at     = DateTime.UtcNow,
        }, ct);
    }

    /// <summary>Live map + ops board while a driver is on an active job.</summary>
    public static Task DriverLocationAsync(
        IRealtimeService realtime,
        string? market,
        string driverId,
        DriverLocationEvent location,
        CancellationToken ct = default)
    {
        if (!AdminLocationThrottle.Allow(location.TripId))
            return Task.CompletedTask;

        var m = NormalizeMarket(market);
        return SafePublishAsync(realtime, m, "driver.location", new
        {
            trip_id    = location.TripId,
            driver_id  = driverId,
            lat        = location.Lat,
            lng        = location.Lng,
            heading    = location.Heading,
            eta_min    = location.EtaMin,
            distance_m = location.DistanceM,
            leg        = location.Leg,
            at         = DateTime.UtcNow,
        }, ct);
    }

    private static string VerticalSlug(Vertical vertical) => vertical switch
    {
        Vertical.CoRide  => "co_ride",
        Vertical.Package => "package",
        _                => "ride",
    };

    private static async Task SafePublishAsync(
        IRealtimeService realtime, string market, string eventName, object payload, CancellationToken ct)
    {
        try
        {
            await realtime.PublishToAdminAsync(market, eventName, payload, ct);
        }
        catch
        {
            /* Realtime is best-effort; REST remains source of truth. */
        }
    }
}
