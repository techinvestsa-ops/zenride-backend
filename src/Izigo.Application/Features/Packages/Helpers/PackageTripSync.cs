using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Realtime.Dtos;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Packages.Helpers;

/// <summary>
/// Packages are tracked in the Packages table; dispatch reuses the Trip job engine
/// linked by shared QuoteId. These helpers keep both in sync.
/// </summary>
internal static class PackageTripSync
{
    public static Task<Package?> FindByTripAsync(
        IApplicationDbContext db, Trip trip, CancellationToken ct)
        => string.IsNullOrEmpty(trip.QuoteId)
            ? Task.FromResult<Package?>(null)
            : db.Packages.FirstOrDefaultAsync(p => p.QuoteId == trip.QuoteId, ct);

    public static async Task<Package?> FindByIdOrTripAsync(
        IApplicationDbContext db, string idOrTripId, CancellationToken ct)
    {
        var package = await db.Packages.FirstOrDefaultAsync(p => p.Id == idOrTripId, ct);
        if (package != null) return package;

        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == idOrTripId, ct);
        return trip == null ? null : await FindByTripAsync(db, trip, ct);
    }

    public static async Task<PackageCourierInfo?> BuildCourierInfoAsync(
        IApplicationDbContext db, string courierId, CancellationToken ct)
    {
        var dp = await db.DriverProfiles
            .Include(d => d.User)
            .Include(d => d.Vehicles.Where(v => v.IsActive))
            .FirstOrDefaultAsync(d => d.UserId == courierId, ct);

        if (dp == null) return null;

        var vehicle = dp.Vehicles.FirstOrDefault();
        return new PackageCourierInfo(dp.User.FirstName, vehicle?.Plate);
    }

    public static int ProgressFor(PackageStatus status) => status switch
    {
        PackageStatus.Searching  => 10,
        PackageStatus.Matched    => 30,
        PackageStatus.PickedUp   => 55,
        PackageStatus.InTransit  => 80,
        PackageStatus.Delivered  => 100,
        PackageStatus.Cancelled  => 0,
        PackageStatus.Returned   => 0,
        _                        => 0,
    };

    public static async Task PublishStatusAsync(
        IRealtimeService realtime,
        IApplicationDbContext db,
        Package pkg,
        CancellationToken ct,
        int? etaMin = null)
    {
        PackageCourierInfo? courier = null;
        if (pkg.CourierId != null)
            courier = await BuildCourierInfoAsync(db, pkg.CourierId, ct);

        await realtime.PublishToUserAsync(pkg.SenderId, "package.status_changed",
            new PackageStatusChangedEvent(
                pkg.TrackingId,
                pkg.Status.ToString().ToLower(),
                courier,
                ProgressFor(pkg.Status),
                etaMin), ct);
    }

    /// <summary>Maps driver job state to package status and applies courier/timestamp fields.</summary>
    public static void ApplyTripState(Package pkg, Trip trip)
    {
        if (trip.DriverId != null)
            pkg.CourierId = trip.DriverId;

        pkg.Status = trip.JobState switch
        {
            JobState.Accepted or JobState.EnRouteToPickup or JobState.ArrivedAtPickup
                => PackageStatus.Matched,
            JobState.PickedUp => PackageStatus.PickedUp,
            JobState.EnRouteToDropoff or JobState.ArrivedAtDropoff
                => PackageStatus.InTransit,
            JobState.CancelledByRider or JobState.CancelledByDriver or
            JobState.CancelledByAdmin or JobState.Expired
                => PackageStatus.Cancelled,
            _ => pkg.Status,
        };

        if (pkg.Status == PackageStatus.PickedUp && pkg.PickedUpAt == null)
            pkg.PickedUpAt = DateTime.UtcNow;

        if (pkg.Status == PackageStatus.Cancelled && pkg.CancelledAt == null)
            pkg.CancelledAt = DateTime.UtcNow;
    }
}
