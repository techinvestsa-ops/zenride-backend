using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Helpers;
using Izigo.Application.Features.Driver.Dtos;
using Izigo.Application.Features.Rides.Helpers;
using Izigo.Domain.Enums;
using Izigo.Application.Common.Settings;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Izigo.Application.Features.Driver.Queries;

// ── GET /driver/status ────────────────────────────────────────────────────────

public record GetDriverStatusQuery(string DriverId) : IRequest<DriverStatusDto>;

public class GetDriverStatusHandler(IApplicationDbContext db)
    : IRequestHandler<GetDriverStatusQuery, DriverStatusDto>
{
    public async Task<DriverStatusDto> Handle(GetDriverStatusQuery req, CancellationToken ct)
    {
        var dp = await db.DriverProfiles
            .Include(d => d.DriverWallet)
            .FirstOrDefaultAsync(d => d.UserId == req.DriverId, ct)
            ?? throw new KeyNotFoundException("Driver not found.");

        var cashSettlement = dp.DriverWallet?.PendingCashSettlement ?? 0;
        const long CashCap = 10_000;   // configurable per market

        return new DriverStatusDto(
            IsOnline: dp.IsOnline,
            KycStatus: dp.KycStatus.ToString().ToLower(),
            OnboardingComplete: dp.OnboardingComplete,
            VerticalsAllowed: dp.VerticalsAllowed.Select(v => v.ToString().ToLower()).ToArray(),
            PendingCashSettlement: cashSettlement,
            CashCapBlocked: cashSettlement >= CashCap);
    }
}

// ── GET /driver/home ──────────────────────────────────────────────────────────

public record GetDriverHomeQuery(string DriverId) : IRequest<DriverHomeDto>;

public class GetDriverHomeHandler(IApplicationDbContext db)
    : IRequestHandler<GetDriverHomeQuery, DriverHomeDto>
{
    public async Task<DriverHomeDto> Handle(GetDriverHomeQuery req, CancellationToken ct)
    {
        var dp = await db.DriverProfiles
            .Include(d => d.DriverWallet)
                .ThenInclude(w => w!.Transactions)
            .FirstOrDefaultAsync(d => d.UserId == req.DriverId, ct)
            ?? throw new KeyNotFoundException("Driver not found.");

        var today = DateTime.UtcNow.Date;
        var tripsToday = await db.Trips
            .CountAsync(t => t.DriverId == req.DriverId &&
                             t.JobState == JobState.Completed &&
                             t.CompletedAt >= today, ct);

        var earningsToday = dp.DriverWallet?.Transactions
            .Where(t => t.CreatedAt >= today)
            .Sum(t => t.Amount) ?? 0;

        // Active job for cold-start resume
        var activeTrip = await db.Trips
            .FirstOrDefaultAsync(t => t.DriverId == req.DriverId &&
                                      !RideProjector.TerminalStates.Contains(t.JobState), ct);

        JobOfferDto? activeJob = null;
        if (activeTrip != null)
            activeJob = JobOfferMapper.ToOffer(activeTrip, 0);

        return new DriverHomeDto(
            IsOnline: dp.IsOnline,
            KycStatus: dp.KycStatus.ToString().ToLower(),
            Today: new DriverTodaySummaryDto(
                tripsToday, earningsToday,
                dp.OnlineSecondsToday / 60, "XOF"),
            ActiveJob: activeJob);
    }
}

// ── GET /driver/demand ────────────────────────────────────────────────────────

public record GetDemandQuery(string DriverId) : IRequest<DemandHeatmapDto>;

public class GetDemandHandler(IApplicationDbContext db)
    : IRequestHandler<GetDemandQuery, DemandHeatmapDto>
{
    // ~0.03 degrees ≈ 3 km — used as the radius to assign a trip request to a zone
    private const double ZoneRadiusDeg = 0.03;

    private static readonly TimeSpan DemandWindow = TimeSpan.FromMinutes(30);

    public async Task<DemandHeatmapDto> Handle(GetDemandQuery req, CancellationToken ct)
    {
        var driver = await db.DriverProfiles
            .Where(d => d.UserId == req.DriverId)
            .Select(d => new { d.LastLat, d.LastLng })
            .FirstOrDefaultAsync(ct);

        // Get the market from the driver's profile user
        var user = await db.Users
            .Where(u => u.Id == req.DriverId)
            .Select(u => new { u.Id })
            .FirstOrDefaultAsync(ct);

        // Live zones for this region
        var zones = await db.Zones
            .Where(z => z.Status == "live")
            .Select(z => new { z.CenterLat, z.CenterLng, z.Name })
            .ToListAsync(ct);

        if (zones.Count == 0)
            return new DemandHeatmapDto(Zones: []);

        // Where riders asked for a ride in the last half hour, whatever became
        // of it: trips that found nobody are the demand drivers should go to.
        // Counting only trips searching right now left the map empty at
        // normal volumes. Held scheduled rides are not demand yet.
        var now = DateTime.UtcNow;
        var dueBy = ScheduledRides.DueCutoff(now);
        var since = now - DemandWindow;
        var broadcastingPickups = await db.Trips
            .Where(t => t.CreatedAt >= since &&
                        t.Vertical != Vertical.CoRide &&
                        (t.ScheduledAt == null || t.ScheduledAt <= dueBy))
            .Select(t => new { t.PickupLat, t.PickupLng })
            .ToListAsync(ct);

        var result = zones.Select(zone =>
        {
            var nearby = broadcastingPickups.Count(t =>
                Math.Abs((double)(t.PickupLat - zone.CenterLat)) <= ZoneRadiusDeg &&
                Math.Abs((double)(t.PickupLng - zone.CenterLng)) <= ZoneRadiusDeg);

            // 0 = no demand  1 = low  2 = medium  3 = high
            var demandLevel = nearby switch
            {
                0    => 0,
                < 3  => 1,
                < 8  => 2,
                _    => 3
            };

            return new HeatmapZoneDto((double)zone.CenterLat, (double)zone.CenterLng, demandLevel);
        }).ToArray();

        return new DemandHeatmapDto(Zones: result);
    }
}

// ── GET /driver/jobs/offer ────────────────────────────────────────────────────

public record GetJobOfferQuery(string DriverId) : IRequest<JobOfferDto?>;

public class GetJobOfferHandler(
    IApplicationDbContext db,
    IJobDispatcher jobs,
    IOptions<DispatchSettings> dispatchOptions)
    : IRequestHandler<GetJobOfferQuery, JobOfferDto?>
{
    public async Task<JobOfferDto?> Handle(GetJobOfferQuery req, CancellationToken ct)
    {
        var dp = await db.DriverProfiles
            .FirstOrDefaultAsync(d => d.UserId == req.DriverId, ct);

        if (dp == null) return null;

        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.DriverId == req.DriverId &&
                                      t.JobState == JobState.Offered, ct);

        var claimed = false;
        if (trip == null)
        {
            trip = await ClaimNearbyBroadcastAsync(db, dp, ct);
            claimed = trip != null;
        }

        if (trip == null) return null;

        var timeoutSec = await db.DispatchConfigs
            .Where(c => c.Market == trip.Market)
            .Select(c => (int?)c.OfferTimeoutSeconds)
            .FirstOrDefaultAsync(ct) ?? dispatchOptions.Value.OfferTimeoutSeconds;

        // Give a claimed offer the same window as a dispatched one, so a driver
        // who ignores it does not hold the trip forever.
        if (claimed)
            jobs.Schedule(trip.Id, "offer_expire", DateTime.UtcNow.AddSeconds(timeoutSec));

        // Time left on this driver's offer, so a driver who sees it on a later
        // poll gets the real remainder, not a fresh full countdown.
        var offeredAt = await db.TripStateHistories
            .Where(h => h.TripId == trip.Id && h.State == JobState.Offered && h.ActorId == req.DriverId)
            .OrderByDescending(h => h.OccurredAt)
            .Select(h => (DateTime?)h.OccurredAt)
            .FirstOrDefaultAsync(ct);
        var elapsed = offeredAt is { } at ? (int)(DateTime.UtcNow - at).TotalSeconds : 0;
        var remaining = Math.Clamp(timeoutSec - elapsed, 1, timeoutSec);

        int pickupDistM = 0;
        if (dp.LastLat.HasValue && dp.LastLng.HasValue)
        {
            var dlat = (double)(dp.LastLat.Value - trip.PickupLat);
            var dlng = (double)(dp.LastLng.Value - trip.PickupLng);
            pickupDistM = (int)(Math.Sqrt(dlat * dlat + dlng * dlng) * 111_320);
        }
        return JobOfferMapper.ToOffer(trip, pickupDistM, remaining);
    }

    /// <summary>A driver with a destination set only takes trips towards it.</summary>
    private static bool FitsDestination(Domain.Entities.DriverProfile dp, Domain.Entities.Trip trip) =>
        dp.DestinationLat is not { } destLat || dp.DestinationLng is not { } destLng ||
        DriverDestination.Fits(
            (double)dp.LastLat!.Value, (double)dp.LastLng!.Value,
            (double)destLat, (double)destLng,
            (double)trip.PickupLat, (double)trip.PickupLng,
            (double)trip.DropoffLat, (double)trip.DropoffLng);

    private static async Task<Domain.Entities.Trip?> ClaimNearbyBroadcastAsync(
        IApplicationDbContext db, Domain.Entities.DriverProfile dp, CancellationToken ct)
    {
        if (!dp.IsOnline ||
            dp.KycStatus != KycStatus.Approved ||
            !dp.OnboardingComplete ||
            dp.LastLat == null ||
            dp.LastLng == null ||
            dp.LastLocationAt < DateTime.UtcNow.AddMinutes(-8))
        {
            return null;
        }

        var dueBy = ScheduledRides.DueCutoff(DateTime.UtcNow);
        var candidates = await db.Trips
            .Where(t => t.JobState == JobState.Broadcasting &&
                        t.Vertical == Vertical.Ride &&
                        t.DriverId == null &&
                        (t.ScheduledAt == null || t.ScheduledAt <= dueBy))
            .OrderBy(t => t.CreatedAt)
            .Take(30)
            .ToListAsync(ct);

        const int radiusM = 8000;
        Domain.Entities.Trip? match = null;
        foreach (var candidate in candidates)
        {
            var dlat = (double)(dp.LastLat.Value - candidate.PickupLat);
            var dlng = (double)(dp.LastLng.Value - candidate.PickupLng);
            var meters = Math.Sqrt(dlat * dlat + dlng * dlng) * 111_320;
            if (meters <= radiusM && FitsDestination(dp, candidate))
            {
                match = candidate;
                break;
            }
        }

        if (match == null) return null;

        match.DriverId = dp.UserId;
        match.JobState = JobState.Offered;
        db.TripStateHistories.Add(new Domain.Entities.TripStateHistory
        {
            TripId = match.Id,
            State = JobState.Offered,
            OccurredAt = DateTime.UtcNow,
            Actor = "system",
            ActorId = dp.UserId
        });
        await db.SaveChangesAsync(ct);
        return match;
    }
}
