using Izigo.Domain.Enums;

namespace Izigo.Application.Features.Rides.Helpers;

/// <summary>
/// Projects the driver's canonical job state to the rider-facing status.
/// Contract §SHARED: "Compute the rider status from the trip state on the way out; never store both."
/// </summary>
public static class RideProjector
{
    public static string ToRiderStatus(JobState state) => state switch
    {
        JobState.Broadcasting or
        JobState.Offered or
        JobState.RejectedByDriver   => "searching",

        JobState.Expired            => "no_drivers_found",

        JobState.Accepted           => "driver_assigned",
        JobState.EnRouteToPickup    => "driver_arriving",
        JobState.ArrivedAtPickup    => "driver_arrived",

        JobState.PickedUp or
        JobState.EnRouteToDropoff or
        JobState.ArrivedAtDropoff   => "in_progress",

        JobState.Completed          => "completed",
        JobState.Returned           => "completed",   // + returned flag surfaced separately
        JobState.Disputed           => "completed",   // + disputed flag

        JobState.CancelledByRider or
        JobState.CancelledByDriver or
        JobState.CancelledByAdmin   => "cancelled",

        _                           => "searching"
    };

    public static bool IsTerminal(JobState state) => state is
        JobState.Completed or JobState.CancelledByRider or JobState.CancelledByDriver or
        JobState.CancelledByAdmin or JobState.Expired or JobState.RejectedByDriver or
        JobState.Returned or JobState.Disputed;

    public static bool IsActive(JobState state) => !IsTerminal(state);
}

/// <summary>Generates the human-readable ride codes shown on receipts and in support.</summary>
public static class TripCode
{
    private static int _counter;
    private static readonly Lock Lock = new();

    public static string GenerateRide()
    {
        int n;
        lock (Lock) { n = (++_counter % 9999) + 1; }
        return $"ZR-{n:D4}";
    }

    public static string GenerateDelivery()
    {
        int n;
        lock (Lock) { n = (++_counter % 9999) + 1; }
        return $"ZD-{n:D4}-{Random.Shared.Next(1000, 9999)}";
    }
}

/// <summary>Masks phone numbers for display: "+2250700000042" → "+225 07 •• •• 42"</summary>
public static class PhoneMasker
{
    public static string Mask(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone) || phone.Length < 8)
            return phone;

        // Keep country code (up to +3 digits), show first 2 and last 2 digits of subscriber
        var digits = phone.TrimStart('+');
        if (digits.Length <= 6) return phone;

        // Simple approach: keep leading 6 chars, mask middle, keep last 2
        var prefix = phone[..Math.Min(6, phone.Length)];
        var suffix = phone[^2..];
        return $"{prefix} •• •• {suffix}";
    }
}
