namespace Izigo.Application.Features.Rides.Helpers;

/// <summary>
/// A scheduled ride sits in Broadcasting with a future ScheduledAt and is
/// "held": invisible to dispatch, driver claims, demand maps and the rider's
/// active ride until <see cref="Lead"/> before its time, when dispatch starts.
/// </summary>
public static class ScheduledRides
{
    /// <summary>Dispatch starts this long before the pickup time.</summary>
    public static readonly TimeSpan Lead = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan MinAhead = TimeSpan.FromMinutes(20);
    public static readonly TimeSpan MaxAhead = TimeSpan.FromDays(7);

    /// <summary>Trips scheduled at or before this are due for dispatch.</summary>
    public static DateTime DueCutoff(DateTime utcNow) => utcNow + Lead;

    /// <summary>Validates a requested pickup time and returns it in UTC.</summary>
    public static DateTime Validate(DateTime requested, DateTime utcNow)
    {
        var at = requested.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(requested, DateTimeKind.Utc)
            : requested.ToUniversalTime();
        if (at < utcNow + MinAhead)
            throw new ArgumentException(
                $"VALIDATION_ERROR: Schedule a ride at least {MinAhead.TotalMinutes:0} minutes ahead.");
        if (at > utcNow + MaxAhead)
            throw new ArgumentException(
                $"VALIDATION_ERROR: Rides can be scheduled up to {MaxAhead.TotalDays:0} days ahead.");
        return at;
    }
}
