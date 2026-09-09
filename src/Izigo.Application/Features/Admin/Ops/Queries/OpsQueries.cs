using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Application.Common.Settings;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Izigo.Application.Features.Admin.Ops.Queries;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record LiveJobRiderDto(string Id, string Name, string PhoneMasked);
public record LiveJobDriverDto(string Id, string Name, string? Plate);
public record LiveJobDto(string Id, string State, LiveJobRiderDto Rider, LiveJobDriverDto? Driver,
    string PickupLabel, string DropoffLabel, long FareGross, string Currency,
    string PaymentMethod, DateTime CreatedAt);

public record LiveOpsDto(
    IEnumerable<LiveJobDto> Jobs,
    int OffersPending,
    int UnmatchedRequests,
    decimal? AvgPickupEtaMin);

public record DriverOnlineDto(string Id, string Name, string? Plate, decimal? Lat, decimal? Lng,
    decimal? Heading, string? Zone, string State, string? Vertical, DateTime? LastPingAt);

public record DemandZoneDto(string ZoneId, string ZoneName, string DemandLevel, decimal Multiplier,
    int UnmatchedCount, string? Polygon);

public record JobTrackDto(string JobId, decimal? DriverLat, decimal? DriverLng,
    string? RemainingPolyline, decimal PickupLat, decimal PickupLng,
    decimal DropoffLat, decimal DropoffLng, int? EtaMinutes);

public record HeatmapCellDto(decimal Lat, decimal Lng, decimal Weight);

public record ZoneGeoJsonDto(string Id, string Name, string Status, string? Polygon, decimal CenterLat, decimal CenterLng);

public record LocationPointDto(decimal Lat, decimal Lng, decimal? Heading, decimal? Speed, DateTime RecordedAt);

// ── GET /admin/ops/live ───────────────────────────────────────────────────────

public record GetLiveOpsQuery(string Market) : IRequest<object>;

public class GetLiveOpsHandler(IApplicationDbContext db, IOptions<OpsSettings> opsOptions) : IRequestHandler<GetLiveOpsQuery, object>
{
    private static readonly JobState[] ActiveStates =
    [
        JobState.Broadcasting, JobState.Offered, JobState.Accepted,
        JobState.EnRouteToPickup, JobState.ArrivedAtPickup,
        JobState.PickedUp, JobState.EnRouteToDropoff, JobState.ArrivedAtDropoff
    ];

    public async Task<object> Handle(GetLiveOpsQuery req, CancellationToken ct)
    {
        var trips = await db.Trips
            .Where(t => t.Market == req.Market && ActiveStates.Contains(t.JobState))
            .OrderByDescending(t => t.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        var riderIds  = trips.Select(t => t.RiderId).Distinct().ToList();
        var driverIds = trips.Select(t => t.DriverId).Where(d => d != null).Distinct().ToList();

        var riders = await db.Users
            .Where(u => riderIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Phone })
            .ToListAsync(ct);

        var drivers = await db.DriverProfiles
            .Where(d => driverIds.Contains(d.Id))
            .Select(d => new { d.Id, d.User.FirstName, d.User.LastName })
            .ToListAsync(ct);

        var vehicles = await db.Vehicles
            .Where(v => driverIds.Contains(v.DriverId) && v.IsActive)
            .Select(v => new { v.DriverId, v.Plate })
            .ToListAsync(ct);

        var riderMap  = riders.ToDictionary(u => u.Id);
        var driverMap = drivers.ToDictionary(d => d.Id);
        var plateMap  = vehicles.ToDictionary(v => v.DriverId, v => v.Plate);

        var jobs = trips.Select(t =>
        {
            riderMap.TryGetValue(t.RiderId, out var rider);
            LiveJobDriverDto? driver = null;
            if (t.DriverId is not null && driverMap.TryGetValue(t.DriverId, out var dp))
                driver = new LiveJobDriverDto(t.DriverId, $"{dp.FirstName} {dp.LastName}".Trim(),
                    plateMap.GetValueOrDefault(t.DriverId));

            return new LiveJobDto(
                t.Id,
                ToDisplayState(t.JobState),
                new LiveJobRiderDto(t.RiderId, rider is not null ? $"{rider.FirstName} {rider.LastName}".Trim() : "—",
                    rider is not null ? MaskPhone(rider.Phone) : "—"),
                driver,
                t.PickupLabel, t.DropoffLabel,
                t.FareGross, t.Currency,
                t.PaymentMethod.ToString(),
                t.CreatedAt);
        }).ToArray();

        var offersPending = trips.Count(t => t.JobState is JobState.Broadcasting or JobState.Offered);

        // Unmatched: still Broadcasting after 5 min
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        var unmatched = trips.Count(t => t.JobState == JobState.Broadcasting && t.CreatedAt <= cutoff);

        // Avg pickup ETA: for EnRouteToPickup trips, estimate from last driver location
        decimal? avgEta = null;
        var enRoute = trips.Where(t => t.JobState == JobState.EnRouteToPickup && t.DriverId != null).ToList();
        if (enRoute.Any())
        {
            var driverLocs = await db.DriverProfiles
                .Where(d => enRoute.Select(e => e.DriverId).Contains(d.Id))
                .Select(d => new { d.Id, d.LastLat, d.LastLng })
                .ToListAsync(ct);
            var locMap = driverLocs.ToDictionary(d => d.Id);

            var etas = enRoute
                .Select(t =>
                {
                    if (t.DriverId is null || !locMap.TryGetValue(t.DriverId, out var loc)
                        || loc.LastLat is null || loc.LastLng is null) return (double?)null;
                    var distKm = HaversineKm((double)loc.LastLat, (double)loc.LastLng,
                        (double)t.PickupLat, (double)t.PickupLng);
                    return distKm / opsOptions.Value.EtaSpeedKmh * 60.0;
                })
                .Where(e => e.HasValue)
                .Select(e => e!.Value)
                .ToList();

            if (etas.Any())
                avgEta = Math.Round((decimal)etas.Average(), 1);
        }

        return AdminApiResponse.Ok(new LiveOpsDto(jobs, offersPending, unmatched, avgEta));
    }

    internal static string ToDisplayState(JobState state) => state switch
    {
        JobState.Broadcasting    => "searching",
        JobState.Offered         => "searching",
        JobState.Accepted        => "driver_assigned",
        JobState.EnRouteToPickup => "driver_assigned",
        JobState.ArrivedAtPickup => "driver_arrived",
        JobState.PickedUp        => "in_progress",
        JobState.EnRouteToDropoff => "in_progress",
        JobState.ArrivedAtDropoff => "in_progress",
        JobState.Completed       => "completed",
        JobState.CancelledByRider   => "cancelled",
        JobState.CancelledByDriver  => "cancelled",
        JobState.CancelledByAdmin   => "cancelled",
        JobState.Expired         => "no_drivers_found",
        JobState.Disputed        => "disputed",
        _                        => state.ToString().ToLower()
    };

    internal static string MaskPhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone) || phone.Length < 8) return "••••••";
        var cc   = phone.Length >= 4 ? phone[..4] : phone;
        var n2   = phone.Length >= 6 ? phone[4..6] : "••";
        var last = phone.Length >= 2 ? phone[^2..] : "••";
        return $"{cc} {n2} •• •• {last}";
    }

    internal static double HaversineKm(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6371;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLng = (lng2 - lng1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180)
              * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}

// ── GET /admin/ops/drivers-online  (also serves A24 map) ─────────────────────
// Per driver: id, name, plate, lat, lng, heading, zone, state=idle|on_trip, vertical, last_ping_at
// Filterable by zone, vertical and state. Cap server-side above a few hundred drivers.

public record GetDriversOnlineQuery(string Market, string? Zone, string? Vertical, string? State)
    : IRequest<object>;

public class GetDriversOnlineHandler(IApplicationDbContext db)
    : IRequestHandler<GetDriversOnlineQuery, object>
{
    public async Task<object> Handle(GetDriversOnlineQuery req, CancellationToken ct)
    {
        var drivers = await db.DriverProfiles
            .Where(d => d.IsOnline && d.LastLat != null && d.LastLng != null)
            .Select(d => new
            {
                d.Id,
                d.User.FirstName,
                d.User.LastName,
                d.LastLat,
                d.LastLng,
                d.LastHeading,
                d.LastLocationAt,
                d.VerticalsAllowed
            })
            .Take(500) // cap server-side
            .ToListAsync(ct);

        var driverIds = drivers.Select(d => d.Id).ToList();

        var vehicles = await db.Vehicles
            .Where(v => driverIds.Contains(v.DriverId) && v.IsActive)
            .Select(v => new { v.DriverId, v.Plate })
            .ToListAsync(ct);

        var plateMap = vehicles.ToDictionary(v => v.DriverId, v => v.Plate);

        // Load zones to assign each driver to a zone
        var zones = await db.Zones
            .Where(z => z.Market == req.Market && z.Status == "live")
            .Select(z => new { z.Id, z.Name, z.CenterLat, z.CenterLng })
            .ToListAsync(ct);

        // Check which trips are active to determine idle vs on_trip
        var activeDriverIds = await db.Trips
            .Where(t => t.Market == req.Market
                     && t.DriverId != null
                     && (t.JobState == JobState.EnRouteToPickup
                      || t.JobState == JobState.ArrivedAtPickup
                      || t.JobState == JobState.PickedUp
                      || t.JobState == JobState.EnRouteToDropoff
                      || t.JobState == JobState.ArrivedAtDropoff))
            .Select(t => t.DriverId!)
            .Distinct()
            .ToListAsync(ct);

        var activeSet = activeDriverIds.ToHashSet();

        var result = drivers.Select(d =>
        {
            // Assign nearest zone
            var lat = (double)d.LastLat!;
            var lng = (double)d.LastLng!;
            var nearestZone = zones
                .Select(z => new { z.Id, z.Name, Dist = GetLiveOpsHandler.HaversineKm(lat, lng, (double)z.CenterLat, (double)z.CenterLng) })
                .OrderBy(z => z.Dist)
                .FirstOrDefault();
            var zoneName = nearestZone?.Dist < 10 ? nearestZone.Name : null; // within 10 km

            var state    = activeSet.Contains(d.Id) ? "on_trip" : "idle";
            var vertical = d.VerticalsAllowed.FirstOrDefault().ToString();

            return new DriverOnlineDto(
                d.Id,
                $"{d.FirstName} {d.LastName}".Trim(),
                plateMap.GetValueOrDefault(d.Id),
                d.LastLat, d.LastLng, d.LastHeading,
                zoneName, state, vertical,
                d.LastLocationAt);
        }).ToList();

        // Apply filters
        if (!string.IsNullOrWhiteSpace(req.Zone))
            result = result.Where(d => d.Zone == req.Zone).ToList();

        if (!string.IsNullOrWhiteSpace(req.Vertical))
            result = result.Where(d => d.Vertical?.ToLower() == req.Vertical.ToLower()).ToList();

        if (!string.IsNullOrWhiteSpace(req.State))
            result = result.Where(d => d.State == req.State).ToList();

        return AdminApiResponse.Ok(result, new AdminPagedMeta(1, result.Count, result.Count, 1));
    }
}

// ── GET /admin/ops/demand ─────────────────────────────────────────────────────

public record GetDemandQuery(string Market) : IRequest<object>;

public class GetDemandHandler(IApplicationDbContext db) : IRequestHandler<GetDemandQuery, object>
{
    public async Task<object> Handle(GetDemandQuery req, CancellationToken ct)
    {
        var zones = await db.Zones
            .Where(z => z.Market == req.Market)
            .Select(z => new { z.Id, z.Name, z.PolygonGeoJson })
            .ToListAsync(ct);

        var zoneIds = zones.Select(z => z.Id).ToList();

        var surges = await db.SurgeZones
            .Where(s => zoneIds.Contains(s.ZoneId) && s.IsActive
                     && (s.ExpiresAt == null || s.ExpiresAt > DateTime.UtcNow))
            .ToDictionaryAsync(s => s.ZoneId, s => s.Multiplier, ct);

        // Unmatched requests per zone — trips in Broadcasting state in each zone
        // (We approximate zone assignment via pickup coordinates + zone center proximity)
        var broadcasting = await db.Trips
            .Where(t => t.Market == req.Market && t.JobState == JobState.Broadcasting)
            .Select(t => new { t.PickupLat, t.PickupLng })
            .ToListAsync(ct);

        var zoneData = zones.Select(z =>
        {
            var multiplier = surges.GetValueOrDefault(z.Id, 1.0m);
            var unmatched  = broadcasting.Count(t =>
                GetLiveOpsHandler.HaversineKm((double)t.PickupLat, (double)t.PickupLng,
                    (double)(z.PolygonGeoJson.Contains("center") ? 0 : 0), 0) < 10);

            // Demand level based on unmatched count
            var level = unmatched switch { > 20 => "critical", > 10 => "high", > 3 => "medium", _ => "low" };

            return new DemandZoneDto(z.Id, z.Name, level, multiplier, unmatched, z.PolygonGeoJson);
        }).ToArray();

        return AdminApiResponse.Ok(zoneData);
    }
}

// ── GET /admin/ops/jobs/{id}/track  (A24) ─────────────────────────────────────

public record GetJobTrackQuery(string JobId) : IRequest<object?>;

public class GetJobTrackHandler(IApplicationDbContext db, IOptions<OpsSettings> opsOptions) : IRequestHandler<GetJobTrackQuery, object?>
{
    public async Task<object?> Handle(GetJobTrackQuery req, CancellationToken ct)
    {
        var trip = await db.Trips
            .Where(t => t.Id == req.JobId)
            .Select(t => new
            {
                t.Id, t.JobState, t.DriverId,
                t.PickupLat, t.PickupLng,
                t.DropoffLat, t.DropoffLng,
                t.EncodedPolyline
            })
            .FirstOrDefaultAsync(ct);

        if (trip is null) return null;

        decimal? driverLat = null, driverLng = null;
        int? etaMinutes = null;

        if (trip.DriverId is not null)
        {
            var loc = await db.DriverProfiles
                .Where(d => d.Id == trip.DriverId)
                .Select(d => new { d.LastLat, d.LastLng })
                .FirstOrDefaultAsync(ct);

            driverLat = loc?.LastLat;
            driverLng = loc?.LastLng;

            if (driverLat.HasValue && driverLng.HasValue)
            {
                var distKm = GetLiveOpsHandler.HaversineKm(
                    (double)driverLat, (double)driverLng,
                    (double)trip.PickupLat, (double)trip.PickupLng);
                etaMinutes = (int)Math.Ceiling(distKm / opsOptions.Value.EtaSpeedKmh * 60.0);
            }
        }

        return AdminApiResponse.Ok(new JobTrackDto(
            trip.Id, driverLat, driverLng,
            trip.EncodedPolyline,
            trip.PickupLat, trip.PickupLng,
            trip.DropoffLat, trip.DropoffLng,
            etaMinutes));
    }
}

// ── GET /admin/ops/heatmap  (A24) ────────────────────────────────────────────
// ?metric=requests|unmatched|surge&bucket=hex → cells with weight

public record GetHeatmapQuery(string Market, string Metric) : IRequest<object>;

public class GetHeatmapHandler(IApplicationDbContext db) : IRequestHandler<GetHeatmapQuery, object>
{
    public async Task<object> Handle(GetHeatmapQuery req, CancellationToken ct)
    {
        // Use zone centers as heatmap cells — real hex bucketing requires a spatial library
        var zones = await db.Zones
            .Where(z => z.Market == req.Market)
            .Select(z => new { z.Id, z.CenterLat, z.CenterLng })
            .ToListAsync(ct);

        IEnumerable<HeatmapCellDto> cells;

        if (req.Metric == "surge")
        {
            var zoneIds = zones.Select(z => z.Id).ToList();
            var surges  = await db.SurgeZones
                .Where(s => zoneIds.Contains(s.ZoneId) && s.IsActive
                         && (s.ExpiresAt == null || s.ExpiresAt > DateTime.UtcNow))
                .ToDictionaryAsync(s => s.ZoneId, s => s.Multiplier, ct);
            cells = zones.Select(z =>
                new HeatmapCellDto(z.CenterLat, z.CenterLng,
                    surges.GetValueOrDefault(z.Id, 1.0m)));
        }
        else
        {
            var since = DateTime.UtcNow.AddHours(-1);
            var stateFilter = req.Metric == "unmatched"
                ? new[] { JobState.Broadcasting }
                : new[] { JobState.Broadcasting, JobState.Offered, JobState.Accepted,
                          JobState.EnRouteToPickup, JobState.ArrivedAtPickup,
                          JobState.PickedUp, JobState.EnRouteToDropoff, JobState.ArrivedAtDropoff };

            var requests = await db.Trips
                .Where(t => t.Market == req.Market
                         && stateFilter.Contains(t.JobState)
                         && t.CreatedAt >= since)
                .Select(t => new { t.PickupLat, t.PickupLng })
                .ToListAsync(ct);

            // Bucket trips to nearest zone center
            cells = zones.Select(z =>
            {
                var weight = requests.Count(r =>
                    GetLiveOpsHandler.HaversineKm((double)r.PickupLat, (double)r.PickupLng,
                        (double)z.CenterLat, (double)z.CenterLng) < 5);
                return new HeatmapCellDto(z.CenterLat, z.CenterLng, weight);
            });
        }

        return AdminApiResponse.Ok(cells);
    }
}

// ── GET /admin/zones/geojson  (A24) ──────────────────────────────────────────

public record GetZonesGeoJsonQuery(string Market) : IRequest<object>;

public class GetZonesGeoJsonHandler(IApplicationDbContext db)
    : IRequestHandler<GetZonesGeoJsonQuery, object>
{
    public async Task<object> Handle(GetZonesGeoJsonQuery req, CancellationToken ct)
    {
        var zones = await db.Zones
            .Where(z => z.Market == req.Market)
            .Select(z => new ZoneGeoJsonDto(
                z.Id, z.Name, z.Status,
                z.PolygonGeoJson,
                z.CenterLat, z.CenterLng))
            .ToListAsync(ct);

        return AdminApiResponse.Ok(zones);
    }
}

// ── GET /admin/drivers/{id}/location-history  (A24) ──────────────────────────
// Separate permission + every access is audited

public record GetLocationHistoryQuery(string DriverId, DateTime? From, DateTime? To,
    string StaffId, string StaffName, string Market) : IRequest<object?>;

public class GetLocationHistoryHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<GetLocationHistoryQuery, object?>
{
    public async Task<object?> Handle(GetLocationHistoryQuery req, CancellationToken ct)
    {
        var exists = await db.DriverProfiles.AnyAsync(d => d.Id == req.DriverId, ct);
        if (!exists) return null;

        var query = db.DriverLocationPoints.Where(p => p.DriverId == req.DriverId);

        if (req.From.HasValue)  query = query.Where(p => p.RecordedAt >= req.From.Value);
        if (req.To.HasValue)    query = query.Where(p => p.RecordedAt <= req.To.Value);

        var points = await query
            .OrderBy(p => p.RecordedAt)
            .Take(5000) // cap to avoid huge payloads
            .Select(p => new LocationPointDto(p.Lat, p.Lng, p.Heading, p.Speed, p.RecordedAt))
            .ToListAsync(ct);

        // Audit every access — the spec says this explicitly
        await audit.RecordAsync(
            req.StaffId, req.StaffName,
            AuditAction.LocationHistoryAccess,
            "Driver", req.DriverId,
            reason: $"Viewed location history ({req.From:d}–{req.To:d})",
            market: req.Market,
            ct: ct);

        return AdminApiResponse.Ok(new { driver_id = req.DriverId, points });
    }
}
