using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Rides.Helpers;
using Izigo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Quotes.Helpers;

/// <summary>
/// Demand-based surge for rides, off unless the market's "surge_pricing"
/// flag is on. Compares recent ride requests around the pickup with free
/// drivers nearby; prices rise only when requests clearly outnumber them,
/// and never above <see cref="MaxMultiplier"/>. Riders see it on the quote
/// before booking.
/// </summary>
public static class SurgePricing
{
    public const string FlagKey = "surge_pricing";
    public const decimal MaxMultiplier = 1.5m;

    private static readonly TimeSpan DemandWindow = TimeSpan.FromMinutes(15);
    private const double DemandRadiusDeg = 0.018;   // ~2 km
    private const double SupplyRadiusDeg = 0.027;   // ~3 km
    private const int MinDemand = 4;                // a couple of requests is not a rush

    public static async Task<(decimal Multiplier, string? Reason)> ForPickupAsync(
        IApplicationDbContext db, string market, double lat, double lng, int freshnessMinutes, CancellationToken ct)
    {
        var enabled = await db.FeatureFlags
            .AnyAsync(f => f.Key == FlagKey && f.Market == market && f.Enabled, ct);
        if (!enabled) return (1.0m, null);

        var now = DateTime.UtcNow;
        var since = now - DemandWindow;
        var dueBy = ScheduledRides.DueCutoff(now);
        var (la, lo) = ((decimal)lat, (decimal)lng);
        var dDeg = (decimal)DemandRadiusDeg;
        var sDeg = (decimal)SupplyRadiusDeg;

        var demand = await db.Trips.CountAsync(t =>
            t.Vertical == Vertical.Ride && t.CreatedAt >= since &&
            (t.ScheduledAt == null || t.ScheduledAt <= dueBy) &&
            t.PickupLat >= la - dDeg && t.PickupLat <= la + dDeg &&
            t.PickupLng >= lo - dDeg && t.PickupLng <= lo + dDeg, ct);
        if (demand < MinDemand) return (1.0m, null);

        var fresh = now.AddMinutes(-freshnessMinutes);
        var busy = await db.Trips
            .Where(t => t.DriverId != null && !RideProjector.TerminalStates.Contains(t.JobState))
            .Select(t => t.DriverId!)
            .Distinct()
            .ToListAsync(ct);
        var supply = await db.DriverProfiles.CountAsync(d =>
            d.IsOnline && d.KycStatus == KycStatus.Approved &&
            d.LastLocationAt >= fresh && d.LastLat != null && d.LastLng != null &&
            d.LastLat >= la - sDeg && d.LastLat <= la + sDeg &&
            d.LastLng >= lo - sDeg && d.LastLng <= lo + sDeg &&
            !busy.Contains(d.UserId), ct);

        return Multiplier(demand, supply);
    }

    /// <summary>+0.1 for every half a request per free driver beyond one, capped.</summary>
    public static (decimal Multiplier, string? Reason) Multiplier(int demand, int supply)
    {
        var ratio = demand / (decimal)Math.Max(supply, 1);
        if (ratio < 1.5m) return (1.0m, null);
        var steps = Math.Floor((ratio - 1m) / 0.5m);
        var m = Math.Min(MaxMultiplier, 1.0m + 0.1m * steps);
        return (m, "High demand nearby");
    }
}
