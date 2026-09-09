using Hangfire;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Realtime.Dtos;
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
/// Core dispatch engine. Finds the best available driver for a Broadcasting trip,
/// transitions it to Offered, fires Pusher + FCM, then schedules an expiry that
/// re-runs dispatch if the driver does not respond within OfferTimeoutSeconds.
///
/// Job type convention: "dispatch:{tripId}"
/// Expire callback is scheduled directly on Hangfire (no BackgroundJob DB record).
/// </summary>
public class DispatchJobProcessor(
    ApplicationDbContext db,
    IRealtimeService realtime,
    IPushService push,
    IBackgroundJobClient client,
    IConfiguration config,
    ILogger<DispatchJobProcessor> logger)
{
    private const double MetersPerDegree = 111_000.0;

    // ── Hangfire entry-point ──────────────────────────────────────────────────

    public async Task ProcessAsync(string jobId, CancellationToken ct)
    {
        var job = await db.BackgroundJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null || job.Status != "queued") return;

        job.Status = "running";
        await db.SaveChangesAsync(ct);

        var tripId = job.Type["dispatch:".Length..];
        await DispatchTripAsync(tripId, job, ct);
    }

    // ── Offer-expiry entry-point scheduled inside ProcessAsync ────────────────

    public async Task ExpireOfferAsync(string tripId, CancellationToken ct)
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

        await RequeueAsync(tripId, ct);
    }

    // ── Core dispatch logic ───────────────────────────────────────────────────

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

        // Per-market config, falling back to appsettings DispatchDefaults
        var cfg = await db.DispatchConfigs.FirstOrDefaultAsync(d => d.Market == trip.Market, ct);
        var timeoutSec   = cfg?.OfferTimeoutSeconds ?? config.GetValue("DispatchDefaults:OfferTimeoutSeconds", 15);
        var searchRadius = cfg?.SearchRadiusM       ?? config.GetValue("DispatchDefaults:SearchRadiusM", 3000);

        var radiusDeg          = (decimal)(searchRadius / MetersPerDegree);
        var freshnessCutoff    = DateTime.UtcNow.AddMinutes(
                                     -config.GetValue("Ops:DriverLocationFreshnessMinutes", 5));

        // Drivers already offered this trip (tracked via Offered history entries)
        var triedIds = await db.TripStateHistories
            .Where(h => h.TripId == tripId && h.State == JobState.Offered && h.ActorId != null)
            .Select(h => h.ActorId!)
            .Distinct()
            .ToListAsync(ct);

        // Drivers currently on an active trip
        var busyIds = await db.Trips
            .Where(t => t.DriverId != null &&
                       (t.JobState == JobState.Offered        ||
                        t.JobState == JobState.Accepted        ||
                        t.JobState == JobState.EnRouteToPickup ||
                        t.JobState == JobState.ArrivedAtPickup ||
                        t.JobState == JobState.PickedUp        ||
                        t.JobState == JobState.EnRouteToDropoff||
                        t.JobState == JobState.ArrivedAtDropoff))
            .Select(t => t.DriverId!)
            .ToListAsync(ct);

        // Bounding-box search: online, KYC approved, onboarding done, fresh location
        var candidates = await db.DriverProfiles
            .Where(dp =>
                dp.IsOnline &&
                dp.KycStatus    == KycStatus.Approved &&
                dp.OnboardingComplete &&
                dp.LastLat      != null &&
                dp.LastLng      != null &&
                dp.LastLocationAt > freshnessCutoff &&
                dp.LastLat >= trip.PickupLat - radiusDeg &&
                dp.LastLat <= trip.PickupLat + radiusDeg &&
                dp.LastLng >= trip.PickupLng - radiusDeg &&
                dp.LastLng <= trip.PickupLng + radiusDeg &&
                !triedIds.Contains(dp.UserId) &&
                !busyIds.Contains(dp.UserId))
            .ToListAsync(ct);

        // In-memory: filter by allowed vertical, then rank by distance → acceptance rate
        var eligible = candidates
            .Where(dp => dp.VerticalsAllowed.Contains(trip.Vertical))
            .Select(dp => (
                dp.UserId,
                dp.AcceptanceRate,
                dp.LastLat,
                dp.LastLng,
                DistM: MetersApart(dp.LastLat!.Value, dp.LastLng!.Value, trip.PickupLat, trip.PickupLng)))
            .OrderBy(d => d.DistM)
            .ThenByDescending(d => d.AcceptanceRate)
            .ToList();

        if (eligible.Count == 0)
        {
            logger.LogInformation(
                "[Dispatch] No drivers available — trip={TripId} tried={Tried} busy={Busy}",
                tripId, triedIds.Count, busyIds.Count);
            await MarkNoDriversFoundAsync(trip, job, ct);
            return;
        }

        var best        = eligible[0];
        var distKm      = best.DistM / 1000.0;
        var etaMin      = Math.Max(1, (int)Math.Ceiling(distKm / 0.5)); // ~30 km/h city speed

        // Transition: Broadcasting → Offered
        trip.DriverId = best.UserId;
        trip.JobState = JobState.Offered;

        db.TripStateHistories.Add(new TripStateHistory
        {
            TripId     = trip.Id,
            State      = JobState.Offered,
            OccurredAt = DateTime.UtcNow,
            Actor      = "system",
            ActorId    = best.UserId
        });

        job.Status      = "completed";
        job.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "[Dispatch] trip={TripId} → offered to driver={DriverId} dist={Dist:F1}km eta={Eta}min",
            tripId, best.UserId, distKm, etaMin);

        // Rider info for offer card
        var rider    = await db.Users.FindAsync([trip.RiderId], ct);
        var earnings = EstimateEarnings(trip);

        var offerPayload = new JobOfferedEvent(
            TripId:            trip.Id,
            Vertical:          trip.Vertical.ToString(),
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

        // Realtime: private-driver.{driverId}
        await realtime.PublishToDriverAsync(best.UserId, "job.offered", offerPayload, ct);

        // FCM: high-priority for locked-screen delivery
        var fcmToken = await db.UserDevices
            .Where(d => d.UserId == best.UserId && d.FcmToken != null)
            .OrderByDescending(d => d.UpdatedAt)
            .Select(d => d.FcmToken!)
            .FirstOrDefaultAsync(ct);

        if (fcmToken != null)
            await push.SendAsync(
                fcmToken,
                title:       "New ride request",
                body:        $"{distKm:F1} km · {earnings} {trip.Currency}",
                type:        "job.offered",
                entityId:    trip.Id,
                deepLink:    $"izigo://job/{trip.Id}",
                highPriority: true,
                ct:          ct);

        // Schedule expiry — if no response within timeout, re-broadcast to next driver
        client.Schedule<DispatchJobProcessor>(
            p => p.ExpireOfferAsync(trip.Id, CancellationToken.None),
            TimeSpan.FromSeconds(timeoutSec));
    }

    private async Task MarkNoDriversFoundAsync(Trip trip, DomainBackgroundJob job, CancellationToken ct)
    {
        trip.JobState = JobState.Expired;
        db.TripStateHistories.Add(new TripStateHistory
        {
            TripId     = trip.Id,
            State      = JobState.Expired,
            OccurredAt = DateTime.UtcNow,
            Actor      = "system"
        });
        job.Status      = "completed";
        job.CompletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        if (trip.RiderId != null)
            await realtime.PublishToUserAsync(trip.RiderId, "ride.no_drivers_found",
                new NoDriversFoundEvent(trip.Id, RetryAllowed: true), ct);
    }

    private async Task RequeueAsync(string tripId, CancellationToken ct)
    {
        var newJob = new DomainBackgroundJob { Type = $"dispatch:{tripId}", Status = "queued" };
        db.BackgroundJobs.Add(newJob);
        await db.SaveChangesAsync(ct);
        client.Enqueue<DispatchJobProcessor>(p => p.ProcessAsync(newJob.Id, CancellationToken.None));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static double MetersApart(decimal lat1, decimal lng1, decimal lat2, decimal lng2)
    {
        var dlat = (double)(lat1 - lat2);
        var dlng = (double)(lng1 - lng2);
        return Math.Sqrt(dlat * dlat + dlng * dlng) * MetersPerDegree;
    }

    private static long EstimateEarnings(Trip trip)
    {
        var gross = trip.FareGross + trip.FareServiceFee - trip.FareDiscount;
        return (long)(gross * 0.75m); // 25% commission — matches CompleteJobHandler default
    }
}
