using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Settings;
using Izigo.Application.Features.Rides.Dtos;
using Izigo.Application.Features.Rides.Helpers;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Izigo.Application.Features.Rides.Queries;

// ── GET /rides/active — cold start resume ─────────────────────────────────────

public record GetActiveRideQuery(string UserId) : IRequest<RideDetailDto?>;

public class GetActiveRideHandler(IApplicationDbContext db, IOptions<AppSettings> appOptions, IOptions<QuoteSettings> quoteOptions)
    : IRequestHandler<GetActiveRideQuery, RideDetailDto?>
{
    public async Task<RideDetailDto?> Handle(GetActiveRideQuery req, CancellationToken ct)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t =>
                t.RiderId == req.UserId &&
                !RideProjector.IsTerminal(t.JobState), ct);

        return trip == null ? null : await RideDetailMapper.BuildAsync(trip, db, ct,
            appOptions.Value.ShareBaseUrl, quoteOptions.Value.DefaultCancellationFee);
    }
}

// ── GET /rides/{id} ───────────────────────────────────────────────────────────

public record GetRideQuery(string UserId, string TripId) : IRequest<RideDetailDto>;

public class GetRideHandler(IApplicationDbContext db, IOptions<AppSettings> appOptions, IOptions<QuoteSettings> quoteOptions)
    : IRequestHandler<GetRideQuery, RideDetailDto>
{
    public async Task<RideDetailDto> Handle(GetRideQuery req, CancellationToken ct)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == req.TripId && t.RiderId == req.UserId, ct)
            ?? throw new KeyNotFoundException("Ride not found.");

        return await RideDetailMapper.BuildAsync(trip, db, ct,
            appOptions.Value.ShareBaseUrl, quoteOptions.Value.DefaultCancellationFee);
    }
}

// ── GET /rides/{id}/driver-location — HTTP polling fallback ──────────────────

public record GetDriverLocationQuery(string UserId, string TripId) : IRequest<DriverLocationDto>;

public class GetDriverLocationHandler(IApplicationDbContext db)
    : IRequestHandler<GetDriverLocationQuery, DriverLocationDto>
{
    public async Task<DriverLocationDto> Handle(GetDriverLocationQuery req, CancellationToken ct)
    {
        var driverId = await db.Trips
            .Where(t => t.Id == req.TripId && t.RiderId == req.UserId)
            .Select(t => t.DriverId)
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Ride not found.");

        if (driverId == null)
            throw new InvalidOperationException("CONFLICT: No driver assigned yet.");

        var driver = await db.DriverProfiles
            .Where(d => d.UserId == driverId)
            .Select(d => new { d.LastLat, d.LastLng, d.LastHeading, d.LastLocationAt })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("Driver not found.");

        return new DriverLocationDto(
            Lat: (double)(driver.LastLat ?? 0),
            Lng: (double)(driver.LastLng ?? 0),
            Heading: driver.LastHeading.HasValue ? (double?)driver.LastHeading.Value : null,
            EtaMin: null,
            UpdatedAt: driver.LastLocationAt ?? DateTime.UtcNow);
    }
}

// ── GET /rides/cancellation-reasons ──────────────────────────────────────────

public record GetCancellationReasonsQuery(string Audience, string Language)
    : IRequest<List<CancellationReasonDto>>;

public class GetCancellationReasonsHandler(IApplicationDbContext db)
    : IRequestHandler<GetCancellationReasonsQuery, List<CancellationReasonDto>>
{
    public async Task<List<CancellationReasonDto>> Handle(
        GetCancellationReasonsQuery req, CancellationToken ct)
    {
        var audience = req.Audience.ToLower();
        var reasons = await db.CancellationReasons
            .Where(r => r.IsActive &&
                        (r.Audience == audience || r.Audience == "both") &&
                        (r.Language == req.Language || r.Language == "fr"))
            .OrderBy(r => r.Order)
            .Select(r => new CancellationReasonDto(r.Code, r.Label))
            .ToListAsync(ct);

        return reasons.Count > 0 ? reasons : DefaultReasons(audience);
    }

    private static List<CancellationReasonDto> DefaultReasons(string audience) => audience switch
    {
        "driver" =>
        [
            new("rider_no_show",     "Rider did not show up"),
            new("wrong_pickup",      "Wrong pickup location"),
            new("rider_misbehaving", "Rider misconduct"),
            new("vehicle_issue",     "Vehicle problem"),
            new("other",             "Other reason")
        ],
        _ =>
        [
            new("driver_too_long",    "Driver is taking too long"),
            new("found_another_ride", "Found another ride"),
            new("wrong_details",      "Trip details are wrong"),
            new("emergency",          "Personal emergency"),
            new("other",              "Other reason")
        ]
    };
}

// ── GET /rides (history) ─────────────────────────────────────────────────────

public record GetRidesQuery(
    string UserId,
    string? Status,
    string? Vertical,
    string? Category,
    DateTime? From,
    DateTime? To,
    int Page,
    int PerPage
) : IRequest<(List<RideHistoryItemDto> Items, int Total)>;

public class GetRidesHandler(IApplicationDbContext db)
    : IRequestHandler<GetRidesQuery, (List<RideHistoryItemDto>, int)>
{
    public async Task<(List<RideHistoryItemDto>, int)> Handle(GetRidesQuery req, CancellationToken ct)
    {
        var q = db.Trips.Where(t => t.RiderId == req.UserId);

        if (!string.IsNullOrEmpty(req.Status) &&
            Enum.TryParse<JobState>(req.Status, true, out var state))
            q = q.Where(t => t.JobState == state);

        if (!string.IsNullOrEmpty(req.Vertical) &&
            Enum.TryParse<Vertical>(req.Vertical, true, out var vertical))
            q = q.Where(t => t.Vertical == vertical);

        if (req.From.HasValue) q = q.Where(t => t.CreatedAt >= req.From.Value);
        if (req.To.HasValue)   q = q.Where(t => t.CreatedAt <= req.To.Value);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(t => t.CreatedAt)
            .Skip((req.Page - 1) * req.PerPage)
            .Take(req.PerPage)
            .Select(t => new RideHistoryItemDto(
                t.Id, t.Code, t.Vertical.ToString().ToLower(),
                RideProjector.ToRiderStatus(t.JobState),
                t.ServiceClass.ToString().ToLower(),
                t.PickupLabel, t.DropoffLabel,
                t.FareGross + t.FareServiceFee - t.FareDiscount,
                t.Currency, t.CompletedAt, t.CreatedAt))
            .ToListAsync(ct);

        return (items, total);
    }
}

// ── GET /rides/{id}/receipt ───────────────────────────────────────────────────

public record GetReceiptQuery(string UserId, string TripId) : IRequest<ReceiptDto>;

public class GetReceiptHandler(IApplicationDbContext db)
    : IRequestHandler<GetReceiptQuery, ReceiptDto>
{
    public async Task<ReceiptDto> Handle(GetReceiptQuery req, CancellationToken ct)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == req.TripId && t.RiderId == req.UserId, ct)
            ?? throw new KeyNotFoundException("Ride not found.");

        return new ReceiptDto(
            TripId: trip.Id,
            Code: trip.Code,
            Fare: new RideFareDto(
                trip.FareGross + trip.FareServiceFee - trip.FareDiscount,
                trip.FareBase, trip.FareDistance, trip.FareTime,
                trip.FareServiceFee, trip.FareDiscount,
                trip.FareTip, trip.Currency, true),
            PdfUrl: null);
    }
}

// ── GET /rides/scheduled ─────────────────────────────────────────────────────

public record GetScheduledRidesQuery(string UserId) : IRequest<List<RideHistoryItemDto>>;

public class GetScheduledRidesHandler(IApplicationDbContext db)
    : IRequestHandler<GetScheduledRidesQuery, List<RideHistoryItemDto>>
{
    public async Task<List<RideHistoryItemDto>> Handle(GetScheduledRidesQuery req, CancellationToken ct)
        => await db.Trips
            .Where(t => t.RiderId == req.UserId &&
                        t.ScheduledAt != null &&
                        t.ScheduledAt > DateTime.UtcNow &&
                        t.JobState == JobState.Broadcasting)
            .OrderBy(t => t.ScheduledAt)
            .Select(t => new RideHistoryItemDto(
                t.Id, t.Code, t.Vertical.ToString().ToLower(),
                "scheduled", t.ServiceClass.ToString().ToLower(),
                t.PickupLabel, t.DropoffLabel,
                t.FareGross + t.FareServiceFee, t.Currency,
                t.ScheduledAt, t.CreatedAt))
            .ToListAsync(ct);
}
