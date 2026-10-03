using Hangfire;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Admin.Realtime;
using Izigo.Application.Features.Dispatch;
using Izigo.Application.Features.Packages.Helpers;
using Izigo.Application.Features.Realtime.Dtos;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Izigo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DomainBackgroundJob = Izigo.Domain.Entities.BackgroundJob;
using TripStateHistory    = Izigo.Domain.Entities.TripStateHistory;
using Trip                = Izigo.Domain.Entities.Trip;

namespace Izigo.Infrastructure.Jobs.Processors;

/// <summary>
/// Matches riders to drivers the way a city-scale dispatcher does.
/// Fresh driver locations are grouped into hexagons, a request looks in its
/// own cell and the six touching it (then the next ring if that is empty),
/// and the short list is ranked by road time. Requests that arrive within
/// a couple of seconds are matched together so one rider does not take the
/// only driver another rider can reach.
///
/// Job type convention: "dispatch:{tripId}"
/// </summary>
public class DispatchJobProcessor(
    ApplicationDbContext db,
    IRealtimeService realtime,
    IPushService push,
    IGeoService geo,
    IBackgroundJobClient client,
    IConfiguration config,
    ILogger<DispatchJobProcessor> logger)
{
    private const double MetersPerDegree = 111_000.0;
    private const int MinDriversBeforeExpanding = 3;
    private const int RoadCheckLimit = 12;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task ProcessAsync(string jobId, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var job = await db.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
            if (job is null || job.Status != "queued") return;

            job.Status = "running";
            await db.SaveChangesAsync(ct);

            var tripId = job.Type["dispatch:".Length..];
            await DispatchTripAsync(tripId, job, ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task ExpireOfferAsync(string tripId, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == tripId, ct);
            if (trip is null || trip.JobState != JobState.Offered) return;

            var expiredDriverId = trip.DriverId;

            trip.JobState = JobState.Broadcasting;
            trip.DriverId = null;

            db.TripStateHistories.Add(new TripStateHistory
            {
                TripId     = trip.Id,
                State      = JobState.Broadcasting,
                OccurredAt = DateTime.UtcNow,
                Actor      = "system"
            });
            await db.SaveChangesAsync(ct);

            if (expiredDriverId != null)
                await realtime.PublishToDriverAsync(expiredDriverId, "job.offer_revoked",
                    new JobOfferRevokedEvent(tripId, "expired"), ct);

            logger.LogInformation("[Dispatch] Offer expired — trip={TripId} driver={DriverId}",
                tripId, expiredDriverId);

            await RequeueAsync(tripId, TimeSpan.Zero, ct);
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task DispatchTripAsync(string tripId, DomainBackgroundJob job, CancellationToken ct)
    {
        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == tripId, ct);
        if (trip is null || trip.JobState != JobState.Broadcasting)
        {
            job.Status = "completed";
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        var alreadyOffered = await db.TripStateHistories
            .AnyAsync(h => h.TripId == tripId && h.State == JobState.Offered, ct);
        if (!alreadyOffered)
        {
            var window = config.GetValue("DispatchDefaults:BatchWindowSeconds", 2);
            if (window > 0)
                await Task.Delay(TimeSpan.FromSeconds(window), ct);
        }

        var pending = await db.Trips
            .Where(t => t.Market == trip.Market && t.JobState == JobState.Broadcasting)
            .OrderBy(t => t.CreatedAt)
            .Take(12)
            .ToListAsync(ct);
        if (pending.All(t => t.Id != trip.Id))
            pending.Add(trip);

        var cfg = await db.DispatchConfigs.FirstOrDefaultAsync(d => d.Market == trip.Market, ct);
        var timeoutSec   = cfg?.OfferTimeoutSeconds ?? config.GetValue("DispatchDefaults:OfferTimeoutSeconds", 15);
        var searchRadius = cfg?.SearchRadiusM       ?? config.GetValue("DispatchDefaults:SearchRadiusM", 3000);
        var freshnessCutoff = DateTime.UtcNow.AddMinutes(
            -config.GetValue("Ops:DriverLocationFreshnessMinutes", 5));

        var pools = new List<TripPool>(pending.Count);
        foreach (var waiting in pending)
            pools.Add(await LoadPoolAsync(waiting, searchRadius, freshnessCutoff, ct));

        var scored = new List<TripPool>(pools.Count);
        foreach (var pool in pools)
            scored.Add(await ScoreByRoadAsync(pool, ct));

        var (offers, expires, retries) = Assign(scored);

        foreach (var offer in offers)
            StageOffer(offer.Trip, offer.DriverUserId);

        foreach (var waiting in expires)
        {
            waiting.JobState = JobState.Expired;
            db.TripStateHistories.Add(new TripStateHistory
            {
                TripId     = waiting.Id,
                State      = JobState.Expired,
                OccurredAt = DateTime.UtcNow,
                Actor      = "system"
            });
        }

        var handledIds = offers.Select(o => o.Trip.Id)
            .Concat(expires.Select(t => t.Id))
            .Concat(retries.Select(t => t.Id))
            .ToList();
        await CloseQueuedJobsAsync(handledIds, job.Id, ct);

        job.Status = "completed";
        job.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "[Dispatch] batch trips={Trips} offered={Offered} waiting={Waiting} none={None}",
            pending.Count, offers.Count, retries.Count, expires.Count);

        foreach (var offer in offers)
        {
            client.Schedule<DispatchJobProcessor>(
                p => p.ExpireOfferAsync(offer.Trip.Id, CancellationToken.None),
                TimeSpan.FromSeconds(timeoutSec));
            await PublishOfferAsync(offer, timeoutSec, ct);
            await AdminRealtimeNotify.JobStateChangedAsync(realtime, offer.Trip, ct);
        }

        foreach (var waiting in expires)
        {
            await PublishNoDriversAsync(waiting, ct);
            await AdminRealtimeNotify.JobStateChangedAsync(realtime, waiting, ct);
        }

        foreach (var waiting in retries)
            await RequeueAsync(waiting.Id, TimeSpan.FromSeconds(timeoutSec), ct);
    }

    private async Task<TripPool> LoadPoolAsync(
        Trip trip, int searchRadius, DateTime freshnessCutoff, CancellationToken ct)
    {
        var triedIds = await db.TripStateHistories
            .Where(h => h.TripId == trip.Id && h.State == JobState.Offered && h.ActorId != null)
            .Select(h => h.ActorId!)
            .Distinct()
            .ToListAsync(ct);

        var busyTripIds = await db.Trips
            .Where(t => t.DriverId != null && t.Id != trip.Id &&
                       (t.JobState == JobState.Offered         ||
                        t.JobState == JobState.Accepted        ||
                        t.JobState == JobState.EnRouteToPickup ||
                        t.JobState == JobState.ArrivedAtPickup ||
                        t.JobState == JobState.PickedUp        ||
                        t.JobState == JobState.EnRouteToDropoff||
                        t.JobState == JobState.ArrivedAtDropoff))
            .Select(t => t.DriverId!)
            .ToListAsync(ct);

        var busyPackageIds = await db.Packages
            .Where(p => p.CourierId != null &&
                        (p.Status == PackageStatus.Matched  ||
                         p.Status == PackageStatus.PickedUp ||
                         p.Status == PackageStatus.InTransit))
            .Select(p => p.CourierId!)
            .ToListAsync(ct);

        var busyIds = busyTripIds.Concat(busyPackageIds).Distinct().ToHashSet();
        var radiusDeg = (decimal)(searchRadius / MetersPerDegree);
        var home = HexGrid.FromLatLng((double)trip.PickupLat, (double)trip.PickupLng);
        var maxRing = HexGrid.RingsForRadius(searchRadius);

        var nearby = await db.DriverProfiles
            .Where(dp =>
                dp.IsOnline &&
                dp.User.Status == UserStatus.Active &&
                dp.KycStatus == KycStatus.Approved &&
                dp.OnboardingComplete &&
                dp.LastLat != null &&
                dp.LastLng != null &&
                dp.LastLocationAt > freshnessCutoff &&
                dp.LastLat >= trip.PickupLat - radiusDeg &&
                dp.LastLat <= trip.PickupLat + radiusDeg &&
                dp.LastLng >= trip.PickupLng - radiusDeg &&
                dp.LastLng <= trip.PickupLng + radiusDeg &&
                !triedIds.Contains(dp.UserId))
            .ToListAsync(ct);

        var inReach = new List<DriverCand>();
        for (var ring = 1; ring <= maxRing; ring++)
        {
            var cells = HexGrid.Disk(home, ring);
            inReach = nearby
                .Where(dp => dp.VerticalsAllowed.Contains(trip.Vertical))
                .Select(dp => new DriverCand(
                    dp.UserId,
                    dp.AcceptanceRate,
                    (double)dp.LastLat!.Value,
                    (double)dp.LastLng!.Value,
                    StraightM: MetersApart(dp.LastLat!.Value, dp.LastLng!.Value, trip.PickupLat, trip.PickupLng),
                    Busy: busyIds.Contains(dp.UserId)))
                .Where(d => d.StraightM <= searchRadius && cells.Contains(HexGrid.FromLatLng(d.Lat, d.Lng)))
                .OrderBy(d => d.StraightM)
                .ToList();

            var free = inReach.Count(d => !d.Busy);
            if (free >= MinDriversBeforeExpanding || ring == maxRing)
                break;
        }

        return new TripPool(trip, inReach);
    }

    private async Task<TripPool> ScoreByRoadAsync(TripPool pool, CancellationToken ct)
    {
        var free = pool.Drivers.Where(d => !d.Busy).Take(RoadCheckLimit).ToList();
        if (free.Count == 0)
            return pool;

        var origin = new GeoPoint((double)pool.Trip.PickupLat, (double)pool.Trip.PickupLng);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(3));
        var etas = await geo.GetEtaAsync(
            origin,
            [.. free.Select(d => new GeoPoint(d.Lat, d.Lng))],
            budget.Token);
        ct.ThrowIfCancellationRequested();

        var ranked = new List<DriverCand>(pool.Drivers.Count);
        for (var i = 0; i < free.Count; i++)
        {
            var eta = i < etas.Length ? etas[i] : null;
            if (eta?.DurationS is not int seconds || eta.DistanceM is not int metres)
                continue;
            ranked.Add(free[i] with { RoadM = metres, RoadS = seconds });
        }

        ranked.AddRange(pool.Drivers.Where(d => d.Busy));
        return pool with { Drivers = ranked };
    }

    private static (List<Offer> Offers, List<Trip> Expires, List<Trip> Retries) Assign(List<TripPool> pools)
    {
        var driverIds = pools
            .SelectMany(p => p.Drivers.Where(d => !d.Busy && d.RoadS >= 0))
            .Select(d => d.UserId)
            .Distinct()
            .ToList();

        var offers = new List<Offer>();
        var expires = new List<Trip>();
        var retries = new List<Trip>();
        if (driverIds.Count == 0)
        {
            foreach (var pool in pools)
                (pool.Drivers.Any(d => d.Busy) && pool.Trip.CreatedAt > DateTime.UtcNow.AddMinutes(-2)
                    ? retries
                    : expires).Add(pool.Trip);
            return (offers, expires, retries);
        }

        var column = driverIds.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var cost = new long[pools.Count, driverIds.Count];
        for (var r = 0; r < pools.Count; r++)
        for (var c = 0; c < driverIds.Count; c++)
            cost[r, c] = MinCostMatcher.Forbidden;

        for (var r = 0; r < pools.Count; r++)
        {
            foreach (var driver in pools[r].Drivers.Where(d => !d.Busy && d.RoadS >= 0))
            {
                // One second of road time outweighs acceptance rate.
                var score = driver.RoadS * 100L - (long)(driver.AcceptanceRate * 99m);
                cost[r, column[driver.UserId]] = Math.Max(1, score);
            }
        }

        var chosen = MinCostMatcher.Solve(cost);
        for (var r = 0; r < pools.Count; r++)
        {
            var pool = pools[r];
            if (chosen[r] >= 0)
            {
                var userId = driverIds[chosen[r]];
                var driver = pool.Drivers.First(d => d.UserId == userId);
                offers.Add(new Offer(pool.Trip, driver.UserId, driver.RoadM, driver.RoadS));
                continue;
            }

            var hadADriver = pool.Drivers.Any(d => !d.Busy && d.RoadS >= 0) || pool.Drivers.Any(d => d.Busy);
            var stillYoung = pool.Trip.CreatedAt > DateTime.UtcNow.AddMinutes(-2);
            if (hadADriver && stillYoung)
                retries.Add(pool.Trip);
            else
                expires.Add(pool.Trip);
        }

        return (offers, expires, retries);
    }

    private void StageOffer(Trip trip, string driverUserId)
    {
        trip.DriverId = driverUserId;
        trip.JobState = JobState.Offered;
        db.TripStateHistories.Add(new TripStateHistory
        {
            TripId     = trip.Id,
            State      = JobState.Offered,
            OccurredAt = DateTime.UtcNow,
            Actor      = "system",
            ActorId    = driverUserId
        });
    }

    private async Task PublishOfferAsync(Offer offer, int timeoutSec, CancellationToken ct)
    {
        var trip = offer.Trip;
        var distKm = offer.RoadM / 1000.0;
        var etaMin = Math.Max(1, (int)Math.Ceiling(offer.RoadS / 60.0));
        var rider = await db.Users.FindAsync([trip.RiderId], ct);
        var earnings = EstimateEarnings(trip);

        logger.LogInformation(
            "[Dispatch] trip={TripId} → offered to driver={DriverId} road={Dist:F1}km eta={Eta}min",
            trip.Id, offer.DriverUserId, distKm, etaMin);

        var offerPayload = new JobOfferedEvent(
            TripId:            trip.Id,
            Vertical:          trip.Vertical.ToString().ToLower(),
            State:             "offered",
            ExpiresInS:        timeoutSec,
            OfferedAt:         DateTime.UtcNow,
            Pickup:            new JobLocationInfo(trip.PickupLabel,
                                   (double)trip.PickupLat, (double)trip.PickupLng),
            Dropoff:           new JobLocationInfo(trip.DropoffLabel,
                                   (double)trip.DropoffLat, (double)trip.DropoffLng),
            Customer:          new JobCustomerInfo(
                                   rider?.FirstName ?? "Rider",
                                   (double)(rider?.Rating ?? 5.0m)),
            DistanceKm:        Math.Round(distKm, 1),
            EtaMin:            etaMin,
            EstimatedEarnings: earnings,
            Currency:          trip.Currency,
            PaymentMethod:     trip.PaymentMethod.ToString().ToLower(),
            SubLabel:          null,
            EncodedPolyline:   trip.EncodedPolyline
        );

        await realtime.PublishToDriverAsync(offer.DriverUserId, "job.offered", offerPayload, ct);

        var fcmToken = await db.UserDevices
            .Where(d => d.UserId == offer.DriverUserId && d.FcmToken != null)
            .OrderByDescending(d => d.UpdatedAt)
            .Select(d => d.FcmToken!)
            .FirstOrDefaultAsync(ct);

        if (fcmToken != null)
        {
            var isPackage = trip.Vertical == Vertical.Package;
            await push.SendAsync(
                fcmToken,
                title:        isPackage ? "New delivery request" : "New ride request",
                body:         $"{distKm:F1} km · {earnings} {trip.Currency}",
                type:         "job.offered",
                entityId:     trip.Id,
                deepLink:     $"izigo://job/{trip.Id}",
                highPriority: true,
                ct:           ct);
        }
    }

    private async Task PublishNoDriversAsync(Trip trip, CancellationToken ct)
    {
        if (trip.RiderId == null) return;
        if (trip.Vertical == Vertical.Package)
        {
            var pkg = await PackageTripSync.FindByTripAsync(db, trip, ct);
            if (pkg != null)
                await PackageTripSync.PublishStatusAsync(realtime, db, pkg, ct);
            return;
        }

        await realtime.PublishToUserAsync(trip.RiderId, "ride.no_drivers_found",
            new NoDriversFoundEvent(trip.Id, RetryAllowed: true), ct);
    }

    private async Task CloseQueuedJobsAsync(List<string> tripIds, string exceptJobId, CancellationToken ct)
    {
        var types = tripIds.Select(id => $"dispatch:{id}").ToList();
        var jobs = await db.BackgroundJobs
            .Where(j => j.Status == "queued" && j.Id != exceptJobId && types.Contains(j.Type))
            .ToListAsync(ct);
        foreach (var queued in jobs)
        {
            queued.Status = "completed";
            queued.CompletedAt = DateTime.UtcNow;
        }
    }

    private async Task RequeueAsync(string tripId, TimeSpan delay, CancellationToken ct)
    {
        var newJob = new DomainBackgroundJob { Type = $"dispatch:{tripId}", Status = "queued" };
        db.BackgroundJobs.Add(newJob);
        await db.SaveChangesAsync(ct);
        if (delay <= TimeSpan.Zero)
            client.Enqueue<DispatchJobProcessor>(p => p.ProcessAsync(newJob.Id, CancellationToken.None));
        else
            client.Schedule<DispatchJobProcessor>(p => p.ProcessAsync(newJob.Id, CancellationToken.None), delay);
    }

    private static int MetersApart(decimal lat1, decimal lng1, decimal lat2, decimal lng2)
    {
        var dlat = (double)(lat1 - lat2);
        var dlng = (double)(lng1 - lng2);
        return (int)(Math.Sqrt(dlat * dlat + dlng * dlng) * MetersPerDegree);
    }

    private static long EstimateEarnings(Trip trip)
    {
        var gross = trip.FareGross + trip.FareServiceFee - trip.FareDiscount;
        return (long)(gross * 0.75m);
    }

    private sealed record DriverCand(
        string UserId,
        decimal AcceptanceRate,
        double Lat,
        double Lng,
        int StraightM,
        bool Busy,
        int RoadM = 0,
        int RoadS = -1);

    private sealed record TripPool(Trip Trip, List<DriverCand> Drivers);

    private sealed record Offer(Trip Trip, string DriverUserId, int RoadM, int RoadS);
}
