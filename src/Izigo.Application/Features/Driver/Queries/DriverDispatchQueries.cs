using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Driver.Dtos;
using Izigo.Application.Features.Rides.Helpers;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

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
                                      !RideProjector.IsTerminal(t.JobState), ct);

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

        // Pickup locations of trips currently searching for a driver
        var broadcastingPickups = await db.Trips
            .Where(t => t.JobState == JobState.Broadcasting)
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

public class GetJobOfferHandler(IApplicationDbContext db)
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

        if (trip == null) return null;

        // Approximate pickup distance — real value comes from geo service in production
        int pickupDistM = dp.LastLat.HasValue && dp.LastLng.HasValue ? 1000 : 0;
        return JobOfferMapper.ToOffer(trip, pickupDistM);
    }
}
