using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Driver.Dtos;
using Izigo.Application.Features.Realtime.Dtos;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Driver.Commands;

// ── POST /driver/status — go online / offline ─────────────────────────────────

public record SetDriverStatusCommand(string DriverId, SetStatusRequest Request)
    : IRequest<DriverStatusDto>;

public class SetDriverStatusHandler(IApplicationDbContext db)
    : IRequestHandler<SetDriverStatusCommand, DriverStatusDto>
{
    public async Task<DriverStatusDto> Handle(SetDriverStatusCommand cmd, CancellationToken ct)
    {
        var dp = await db.DriverProfiles
            .Include(d => d.DriverWallet)
            .FirstOrDefaultAsync(d => d.UserId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Driver profile not found.");

        if (cmd.Request.Online)
        {
            if (dp.KycStatus != KycStatus.Approved)
                throw new InvalidOperationException("FORBIDDEN: KYC must be approved before going online.");

            if (!dp.OnboardingComplete)
                throw new InvalidOperationException("FORBIDDEN: Onboarding is not complete.");

            const long CashCap = 10_000;
            var cashOwed = dp.DriverWallet?.PendingCashSettlement ?? 0;
            if (cashOwed >= CashCap)
                throw new InvalidOperationException(
                    $"FORBIDDEN: Cash settlement balance ({cashOwed}) exceeds the cap. Please settle before going online.");
        }

        dp.IsOnline = cmd.Request.Online;

        if (dp.IsOnline && dp.VerticalsAllowed.Count == 0)
        {
            dp.VerticalsAllowed = [Vertical.Ride, Vertical.CoRide, Vertical.Package];
            await SyncVerticalPreferencesAsync(db, cmd.DriverId, dp.VerticalsAllowed, ct);
        }

        if (cmd.Request.Vertical != null)
        {
            var vertical = ParseDriverVertical(cmd.Request.Vertical);
            if (vertical != null)
            {
                if (dp.VerticalsAllowed.Contains(vertical.Value))
                {
                    if (dp.VerticalsAllowed.Count > 1)
                        dp.VerticalsAllowed.Remove(vertical.Value);
                }
                else
                {
                    dp.VerticalsAllowed.Add(vertical.Value);
                }

                await SyncVerticalPreferencesAsync(db, cmd.DriverId, dp.VerticalsAllowed, ct);
            }
        }

        await db.SaveChangesAsync(ct);

        var cashSettlement = dp.DriverWallet?.PendingCashSettlement ?? 0;
        const long Cap = 10_000;
        return new DriverStatusDto(
            IsOnline: dp.IsOnline,
            KycStatus: dp.KycStatus.ToString().ToLower(),
            OnboardingComplete: dp.OnboardingComplete,
            VerticalsAllowed: dp.VerticalsAllowed.Select(v => v.ToString().ToLower()).ToArray(),
            PendingCashSettlement: cashSettlement,
            CashCapBlocked: cashSettlement >= Cap);
    }

    private static Vertical? ParseDriverVertical(string raw) => raw.ToLowerInvariant() switch
    {
        "ride"                    => Vertical.Ride,
        "coride" or "co_ride"     => Vertical.CoRide,
        "package" or "delivery"   => Vertical.Package,
        _                         => Enum.TryParse<Vertical>(raw.Replace("_", ""), true, out var vert)
                                      ? vert : null,
    };

    private static async Task SyncVerticalPreferencesAsync(
        IApplicationDbContext db,
        string driverId,
        List<Vertical> verticals,
        CancellationToken ct)
    {
        var value = string.Join(",", verticals.Select(v => v switch
        {
            Vertical.Ride    => "ride",
            Vertical.CoRide  => "co_ride",
            Vertical.Package => "package",
            _                => v.ToString().ToLower(),
        }));

        var pref = await db.UserPreferences
            .FirstOrDefaultAsync(p => p.UserId == driverId && p.Key == "driver:verticals", ct);

        if (pref != null)
            pref.Value = value;
        else
            db.UserPreferences.Add(new UserPreference
            {
                UserId = driverId,
                Key    = "driver:verticals",
                Value  = value,
            });
    }
}

// ── POST /driver/location — GPS heartbeat ─────────────────────────────────────

public record UpdateLocationCommand(string DriverId, UpdateLocationRequest Request) : IRequest;

public class UpdateLocationHandler(IApplicationDbContext db, IRealtimeService realtime)
    : IRequestHandler<UpdateLocationCommand>
{
    public async Task Handle(UpdateLocationCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;

        var dp = await db.DriverProfiles
            .FirstOrDefaultAsync(d => d.UserId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Driver profile not found.");

        dp.LastLat        = (decimal)req.Lat;
        dp.LastLng        = (decimal)req.Lng;
        dp.LastHeading    = req.Heading.HasValue ? (decimal)req.Heading.Value : dp.LastHeading;
        dp.LastLocationAt = DateTime.UtcNow;

        // Write location history point
        db.DriverLocationPoints.Add(new DriverLocationPoint
        {
            DriverId   = cmd.DriverId,
            Lat        = (decimal)req.Lat,
            Lng        = (decimal)req.Lng,
            Heading    = req.Heading.HasValue ? (decimal)req.Heading.Value : null,
            Speed      = req.Speed.HasValue ? (decimal)req.Speed.Value : null,
            Accuracy   = req.Accuracy.HasValue ? (decimal)req.Accuracy.Value : null,
            RecordedAt = DateTime.UtcNow,
            JobId      = req.JobId,
        });

        await db.SaveChangesAsync(ct);

        // driver.location → presence-trip.{trip_id}  (spec: throttle ~3–5 s on-trip)
        // The active job_id is passed in the batch so we know which trip room to notify.
        if (string.IsNullOrEmpty(req.JobId)) return;

        var trip = await db.Trips
            .Where(t => t.Id == req.JobId && t.DriverId == cmd.DriverId)
            .Select(t => new { t.JobState, t.PickupLat, t.PickupLng, t.DropoffLat, t.DropoffLng })
            .FirstOrDefaultAsync(ct);

        int? etaMin = null, distanceM = null;
        string? leg = null;
        if (trip != null)
        {
            var toDropoff = trip.JobState is JobState.PickedUp or JobState.EnRouteToDropoff or JobState.ArrivedAtDropoff;
            leg = toDropoff ? "dropoff" : "pickup";
            var (lat, lng) = toDropoff
                ? ((double)trip.DropoffLat, (double)trip.DropoffLng)
                : ((double)trip.PickupLat, (double)trip.PickupLng);
            distanceM = (int)LiveEta.RoadDistanceM(req.Lat, req.Lng, lat, lng);
            etaMin = LiveEta.Minutes(distanceM.Value, req.Speed);
        }

        await realtime.PublishToTripAsync(req.JobId, "driver.location",
            new DriverLocationEvent(req.JobId, req.Lat, req.Lng, req.Heading, etaMin, distanceM, leg), ct);
    }
}

/// Cheap per-heartbeat ETA so riders see live times without a Maps call every few seconds.
internal static class LiveEta
{
    // Straight-line distance understates city driving; 1.3 is a common urban detour factor.
    private const double RoadFactor = 1.3;
    private const double CitySpeedMs = 8.3;   // ≈ 30 km/h
    private const double MinSpeedMs  = 3.0;

    public static double RoadDistanceM(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6_371_000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLng = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * r * Math.Asin(Math.Sqrt(a)) * RoadFactor;
    }

    public static int Minutes(int distanceM, double? speedMs)
    {
        // Blend live speed with the city average so a red light doesn't spike the ETA.
        var speed = speedMs is > MinSpeedMs ? (speedMs.Value + CitySpeedMs) / 2 : CitySpeedMs;
        return Math.Max(1, (int)Math.Ceiling(distanceM / speed / 60));
    }
}
