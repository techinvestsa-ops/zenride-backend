using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Application.Features.Admin.Ops.Queries;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Trips.Queries;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record TripListRiderDto(string Id, string Name);
public record TripListDriverDto(string Id, string Name);
public record TripListRowDto(string Id, string Code, string Vertical, TripListRiderDto Rider,
    TripListDriverDto? Driver, string Route, decimal? DistanceKm, long FareGross, string Currency,
    string PaymentMethod, string Status, DateTime CreatedAt);

public record TripDetailRiderDto(string Id, string Name, string PhoneMasked);
public record TripDetailDriverDto(string Id, string Name, string? Plate, decimal? Rating);
public record TripDetailLocationDto(string Label, decimal Lat, decimal Lng);
public record TripDetailFareDto(long Gross, long Base, long Distance, long Time, long Waiting,
    long ServiceFee, long Discount, long Tip);
public record TripDetailSettlementDto(decimal CommissionRate, long Commission, long DriverEarnings,
    long CashCollected, long CashToRemit, long WalletCredit);
public record TripDetailPaymentDto(string? Id, string Method, string? Status, string? Gateway);
public record TripDetailRatingsDto(int? ByRider, int? ByDriver);
public record TripStateHistoryDto(string State, DateTime At, string Actor);
public record TripDetailDto(
    string TripId, string Code, string Vertical, string Status, string Market, string Currency,
    TripDetailRiderDto Rider, TripDetailDriverDto? Driver,
    TripDetailLocationDto Pickup, TripDetailLocationDto Dropoff,
    decimal? DistanceKm, int? DurationMin,
    TripDetailFareDto Fare, TripDetailSettlementDto Settlement,
    TripDetailPaymentDto Payment, TripDetailRatingsDto Ratings,
    IEnumerable<TripStateHistoryDto> StateHistory, IEnumerable<string> ActionsAllowed);

public record TripRouteDto(string TripId, string? EncodedPolyline, LocationPoint[] GpsPoints);
public record LocationPoint(decimal Lat, decimal Lng, DateTime RecordedAt);

public record ExportTripsResult(string JobId, string StatusUrl);

// ── GET /admin/trips ──────────────────────────────────────────────────────────
// Filters: vertical, status, payment_method, pay_status, zone, driver_id, rider_id, from/to, min_fare

public record GetAdminTripsQuery(
    string Market,
    string? Vertical, string? Status, string? PaymentMethod, string? PayStatus,
    string? Zone, string? DriverId, string? RiderId,
    DateTime? From, DateTime? To, long? MinFare,
    string? Q, int Page, int PerPage) : IRequest<object>;

public class GetAdminTripsHandler(IApplicationDbContext db) : IRequestHandler<GetAdminTripsQuery, object>
{
    public async Task<object> Handle(GetAdminTripsQuery req, CancellationToken ct)
    {
        var query = db.Trips.Where(t => t.Market == req.Market).AsQueryable();

        if (!string.IsNullOrWhiteSpace(req.Vertical) &&
            Enum.TryParse<Vertical>(req.Vertical, true, out var vertical))
            query = query.Where(t => t.Vertical == vertical);

        if (!string.IsNullOrWhiteSpace(req.Status) &&
            Enum.TryParse<JobState>(req.Status, true, out var state))
            query = query.Where(t => t.JobState == state);

        if (!string.IsNullOrWhiteSpace(req.PaymentMethod) &&
            Enum.TryParse<PaymentMethod>(req.PaymentMethod, true, out var pm))
            query = query.Where(t => t.PaymentMethod == pm);

        if (!string.IsNullOrWhiteSpace(req.DriverId))
            query = query.Where(t => t.DriverId == req.DriverId);

        if (!string.IsNullOrWhiteSpace(req.RiderId))
            query = query.Where(t => t.RiderId == req.RiderId);

        if (req.From.HasValue) query = query.Where(t => t.CreatedAt >= req.From.Value);
        if (req.To.HasValue)   query = query.Where(t => t.CreatedAt <= req.To.Value);
        if (req.MinFare.HasValue) query = query.Where(t => t.FareGross >= req.MinFare.Value);

        if (!string.IsNullOrWhiteSpace(req.Q))
            query = query.Where(t => t.Code.Contains(req.Q));

        // Facets by status
        var allForFacets = await query.GroupBy(t => t.JobState)
            .Select(g => new { State = g.Key.ToString(), Count = g.Count() })
            .ToListAsync(ct);
        var facets = new Dictionary<string, Dictionary<string, int>>
        {
            ["status"] = allForFacets.ToDictionary(g => g.State.ToLower(), g => g.Count)
        };

        var perPage  = Math.Max(1, Math.Min(100, req.PerPage));
        var total    = await query.CountAsync(ct);
        var lastPage = (int)Math.Ceiling((double)total / perPage);

        var trips = await query
            .OrderByDescending(t => t.CreatedAt)
            .Skip((req.Page - 1) * perPage)
            .Take(perPage)
            .Select(t => new
            {
                t.Id, t.Code, t.Vertical, t.JobState,
                t.RiderId, t.DriverId,
                t.PickupLabel, t.DropoffLabel,
                t.DistanceM, t.FareGross, t.Currency,
                t.PaymentMethod, t.CreatedAt
            })
            .ToListAsync(ct);

        var riderIds  = trips.Select(t => t.RiderId).Distinct().ToList();
        var driverIds = trips.Select(t => t.DriverId).Where(d => d != null).Distinct().ToList();

        var riders = await db.Users
            .Where(u => riderIds.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName })
            .ToListAsync(ct);

        var drivers = await db.DriverProfiles
            .Where(d => driverIds.Contains(d.Id))
            .Select(d => new { d.Id, d.User.FirstName, d.User.LastName })
            .ToListAsync(ct);

        var riderMap  = riders.ToDictionary(u => u.Id);
        var driverMap = drivers.ToDictionary(d => d.Id);

        var rows = trips.Select(t =>
        {
            riderMap.TryGetValue(t.RiderId, out var rider);
            TripListDriverDto? driver = null;
            if (t.DriverId is not null && driverMap.TryGetValue(t.DriverId, out var dp))
                driver = new TripListDriverDto(t.DriverId, $"{dp.FirstName} {dp.LastName}".Trim());

            return new TripListRowDto(
                t.Id, t.Code,
                t.Vertical.ToString().ToLower(),
                new TripListRiderDto(t.RiderId, rider is not null ? $"{rider.FirstName} {rider.LastName}".Trim() : "—"),
                driver,
                $"{t.PickupLabel} → {t.DropoffLabel}",
                t.DistanceM.HasValue ? Math.Round(t.DistanceM.Value / 1000m, 1) : null,
                t.FareGross, t.Currency,
                t.PaymentMethod.ToString().ToLower(),
                GetLiveOpsHandler.ToDisplayState(t.JobState),
                t.CreatedAt);
        }).ToArray();

        var meta = new AdminPagedMeta(req.Page, perPage, total, lastPage, facets);
        return AdminApiResponse.Ok(rows, meta);
    }
}

// ── GET /admin/trips/{id} ─────────────────────────────────────────────────────

public record GetAdminTripDetailQuery(string TripId) : IRequest<TripDetailDto?>;

public class GetAdminTripDetailHandler(IApplicationDbContext db)
    : IRequestHandler<GetAdminTripDetailQuery, TripDetailDto?>
{
    public async Task<TripDetailDto?> Handle(GetAdminTripDetailQuery req, CancellationToken ct)
    {
        var trip = await db.Trips
            .Include(t => t.StateHistory)
            .FirstOrDefaultAsync(t => t.Id == req.TripId, ct);

        if (trip is null) return null;

        var rider = await db.Users
            .Where(u => u.Id == trip.RiderId)
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Phone, u.Rating })
            .FirstOrDefaultAsync(ct);

        TripDetailDriverDto? driver = null;
        if (trip.DriverId is not null)
        {
            var dp = await db.DriverProfiles
                .Where(d => d.Id == trip.DriverId)
                .Select(d => new { d.Id, d.User.FirstName, d.User.LastName, d.User.Rating })
                .FirstOrDefaultAsync(ct);

            var plate = await db.Vehicles
                .Where(v => v.DriverId == trip.DriverId && v.IsActive)
                .Select(v => v.Plate)
                .FirstOrDefaultAsync(ct);

            if (dp is not null)
                driver = new TripDetailDriverDto(dp.Id, $"{dp.FirstName} {dp.LastName}".Trim(),
                    plate, dp.Rating);
        }

        Payment? payment = null;
        if (trip.PaymentId is not null)
            payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == trip.PaymentId, ct);

        var stateHistory = trip.StateHistory
            .OrderBy(h => h.OccurredAt)
            .Select(h => new TripStateHistoryDto(
                GetLiveOpsHandler.ToDisplayState(h.State),
                h.OccurredAt,
                h.Actor))
            .ToArray();

        var actionsAllowed = BuildActionsAllowed(trip);

        return new TripDetailDto(
            trip.Id, trip.Code,
            trip.Vertical.ToString().ToLower(),
            GetLiveOpsHandler.ToDisplayState(trip.JobState),
            trip.Market, trip.Currency,
            rider is not null
                ? new TripDetailRiderDto(rider.Id, $"{rider.FirstName} {rider.LastName}".Trim(),
                    GetLiveOpsHandler.MaskPhone(rider.Phone))
                : new TripDetailRiderDto(trip.RiderId, "—", "—"),
            driver,
            new TripDetailLocationDto(trip.PickupLabel, trip.PickupLat, trip.PickupLng),
            new TripDetailLocationDto(trip.DropoffLabel, trip.DropoffLat, trip.DropoffLng),
            trip.DistanceM.HasValue ? Math.Round(trip.DistanceM.Value / 1000m, 1) : null,
            trip.DurationS.HasValue ? (int)Math.Round(trip.DurationS.Value / 60m) : null,
            new TripDetailFareDto(
                trip.FareGross, trip.FareBase, trip.FareDistance, trip.FareTime,
                trip.FareWaiting, trip.FareServiceFee, trip.FareDiscount, trip.FareTip),
            new TripDetailSettlementDto(
                trip.CommissionRate, trip.CommissionAmount, trip.DriverEarnings,
                trip.CashCollected, trip.CashToRemit, trip.WalletCredit),
            new TripDetailPaymentDto(
                payment?.Id ?? trip.PaymentId,
                trip.PaymentMethod.ToString().ToLower(),
                payment?.Status.ToString().ToLower(),
                payment?.Gateway),
            new TripDetailRatingsDto(trip.RatingByRider, trip.RatingByDriver),
            stateHistory,
            actionsAllowed);
    }

    private static string[] BuildActionsAllowed(Domain.Entities.Trip trip)
    {
        var actions = new List<string>();
        if (trip.JobState == JobState.Completed)
        {
            actions.Add("refund");
            actions.Add("adjust_fare");
        }
        if (trip.JobState != JobState.Disputed)
            actions.Add("reopen");
        return [.. actions];
    }
}

// ── GET /admin/trips/{id}/route ───────────────────────────────────────────────

public record GetTripRouteQuery(string TripId) : IRequest<TripRouteDto?>;

public class GetTripRouteHandler(IApplicationDbContext db) : IRequestHandler<GetTripRouteQuery, TripRouteDto?>
{
    public async Task<TripRouteDto?> Handle(GetTripRouteQuery req, CancellationToken ct)
    {
        var trip = await db.Trips
            .Where(t => t.Id == req.TripId)
            .Select(t => new { t.Id, t.EncodedPolyline, t.DriverId })
            .FirstOrDefaultAsync(ct);

        if (trip is null) return null;

        // GPS trail from DriverLocationPoints recorded during this trip
        var points = await db.DriverLocationPoints
            .Where(p => p.JobId == req.TripId)
            .OrderBy(p => p.RecordedAt)
            .Select(p => new LocationPoint(p.Lat, p.Lng, p.RecordedAt))
            .ToArrayAsync(ct);

        return new TripRouteDto(trip.Id, trip.EncodedPolyline, points);
    }
}

// ── GET /admin/trips/export ───────────────────────────────────────────────────
// Same filters; 202 + job_id for large ranges

public record ExportTripsQuery(string StaffId, string Market,
    string? Vertical, string? Status, DateTime? From, DateTime? To) : IRequest<ExportTripsResult>;

public class ExportTripsHandler(IApplicationDbContext db, IJobDispatcher jobDispatcher)
    : IRequestHandler<ExportTripsQuery, ExportTripsResult>
{
    public async Task<ExportTripsResult> Handle(ExportTripsQuery req, CancellationToken ct)
    {
        var job = new Domain.Entities.BackgroundJob
        {
            Type               = "report_daily_trips",
            Status             = "queued",
            InitiatedByStaffId = req.StaffId
        };
        db.BackgroundJobs.Add(job);
        await db.SaveChangesAsync(ct);

        jobDispatcher.Enqueue(job.Id, job.Type);
        return new(job.Id, $"/api/v1/admin/jobs/{job.Id}");
    }
}
