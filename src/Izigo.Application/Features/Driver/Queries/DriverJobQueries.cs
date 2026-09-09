using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Driver.Dtos;
using Izigo.Application.Features.Rides.Helpers;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Driver.Queries;

// ── GET /driver/jobs/active ───────────────────────────────────────────────────

public record GetActiveJobQuery(string DriverId) : IRequest<JobDetailDto?>;

public class GetActiveJobHandler(IApplicationDbContext db)
    : IRequestHandler<GetActiveJobQuery, JobDetailDto?>
{
    public async Task<JobDetailDto?> Handle(GetActiveJobQuery req, CancellationToken ct)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.DriverId == req.DriverId &&
                                      !RideProjector.IsTerminal(t.JobState), ct);

        return trip == null ? null : await JobDetailMapper.BuildAsync(trip, db, ct);
    }
}

// ── GET /driver/jobs/{id} ─────────────────────────────────────────────────────

public record GetJobQuery(string DriverId, string TripId) : IRequest<JobDetailDto>;

public class GetJobHandler(IApplicationDbContext db)
    : IRequestHandler<GetJobQuery, JobDetailDto>
{
    public async Task<JobDetailDto> Handle(GetJobQuery req, CancellationToken ct)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == req.TripId && t.DriverId == req.DriverId, ct)
            ?? throw new KeyNotFoundException("Job not found.");

        return await JobDetailMapper.BuildAsync(trip, db, ct);
    }
}

// ── GET /driver/jobs ─── history ──────────────────────────────────────────────

public record GetJobsQuery(
    string DriverId,
    string? Vertical,
    DateTime? From,
    DateTime? To,
    int Page,
    int PerPage
) : IRequest<(List<JobHistoryItemDto> Items, int Total)>;

public class GetJobsHandler(IApplicationDbContext db)
    : IRequestHandler<GetJobsQuery, (List<JobHistoryItemDto>, int)>
{
    public async Task<(List<JobHistoryItemDto>, int)> Handle(GetJobsQuery req, CancellationToken ct)
    {
        var q = db.Trips.Where(t => t.DriverId == req.DriverId);

        if (!string.IsNullOrEmpty(req.Vertical) &&
            Enum.TryParse<Vertical>(req.Vertical, true, out var v))
            q = q.Where(t => t.Vertical == v);

        if (req.From.HasValue) q = q.Where(t => t.CreatedAt >= req.From.Value);
        if (req.To.HasValue)   q = q.Where(t => t.CreatedAt <= req.To.Value);

        var total = await q.CountAsync(ct);
        var items = await q
            .OrderByDescending(t => t.CreatedAt)
            .Skip((req.Page - 1) * req.PerPage)
            .Take(req.PerPage)
            .Select(t => new JobHistoryItemDto(
                t.Id, t.Code,
                t.Vertical.ToString().ToLower(),
                t.JobState.ToString().ToLower(),
                t.ServiceClass.ToString().ToLower(),
                t.PickupLabel, t.DropoffLabel,
                t.DriverEarnings, t.Currency,
                t.CompletedAt, t.CreatedAt))
            .ToListAsync(ct);

        return (items, total);
    }
}

// ── Shared detail builder ─────────────────────────────────────────────────────

internal static class JobDetailMapper
{
    public static async Task<JobDetailDto> BuildAsync(
        Domain.Entities.Trip trip, IApplicationDbContext db, CancellationToken ct)
    {
        var rider = await db.Users
            .FirstOrDefaultAsync(u => u.Id == trip.RiderId, ct);

        var state = trip.JobState;
        var isFinal = RideProjector.IsTerminal(state);

        return new JobDetailDto(
            TripId: trip.Id,
            Code: trip.Code,
            Vertical: trip.Vertical.ToString().ToLower(),
            JobState: state.ToString().ToLower(),
            ClassCode: trip.ServiceClass.ToString().ToLower(),
            Pickup: new JobLocationDto(
                trip.PickupLabel, (double)trip.PickupLat, (double)trip.PickupLng),
            Dropoff: new JobLocationDto(
                trip.DropoffLabel, (double)trip.DropoffLat, (double)trip.DropoffLng),
            StartOtp: trip.StartOtp,
            Customer: rider == null
                ? new JobCustomerDto("Customer", "••••••••", 5.0, null, 0)
                : new JobCustomerDto(
                    rider.FirstName,
                    PhoneMasker.Mask(rider.Phone),
                    (double)rider.Rating,
                    rider.PhotoUrl,
                    0),
            Fare: new JobFareDto(
                Total:          trip.FareGross + trip.FareServiceFee - trip.FareDiscount,
                Base:           trip.FareBase,
                Distance:       trip.FareDistance,
                Time:           trip.FareTime,
                ServiceFee:     trip.FareServiceFee,
                Discount:       trip.FareDiscount,
                DriverEarnings: trip.DriverEarnings,
                Currency:       trip.Currency,
                IsFinal:        isFinal),
            PaymentMethod: trip.PaymentMethod.ToString().ToLower(),
            NoteToDriver: trip.NoteToDriver,
            Timestamps: new JobTimestampsDto(
                trip.CreatedAt, trip.AssignedAt, trip.ArrivedAt,
                trip.StartedAt, trip.CompletedAt),
            Actions: new JobActionsDto(
                CanArrive:   state == JobState.Accepted || state == JobState.EnRouteToPickup,
                CanStart:    state == JobState.ArrivedAtPickup,
                CanComplete: state == JobState.ArrivedAtDropoff,
                CanCancel:   !isFinal && state != JobState.PickedUp));
    }
}

// ── Shared offer mapper ───────────────────────────────────────────────────────

internal static class JobOfferMapper
{
    public static JobOfferDto ToOffer(Domain.Entities.Trip trip, int pickupDistM) =>
        new(
            TripId:           trip.Id,
            Code:             trip.Code,
            Vertical:         trip.Vertical.ToString().ToLower(),
            ClassCode:        trip.ServiceClass.ToString().ToLower(),
            Pickup:           new JobLocationDto(
                trip.PickupLabel, (double)trip.PickupLat, (double)trip.PickupLng),
            Dropoff:          new JobLocationDto(
                trip.DropoffLabel, (double)trip.DropoffLat, (double)trip.DropoffLng),
            DistanceM:        trip.DistanceM ?? 0,
            PickupDistanceM:  pickupDistM,
            DurationS:        trip.DurationS ?? 0,
            FareGross:        trip.FareGross + trip.FareServiceFee - trip.FareDiscount,
            Currency:         trip.Currency,
            PaymentMethod:    trip.PaymentMethod.ToString().ToLower(),
            OfferExpiresInSec: 15,
            NoteToDriver:     trip.NoteToDriver);
}
