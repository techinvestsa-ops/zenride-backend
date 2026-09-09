using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Rides.Helpers;
using Izigo.Application.Features.Safety.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Safety.Queries;

// ── GET /trips/shared/{token} — public ────────────────────────────────────────

public record GetSharedTripQuery(string Token) : IRequest<SharedTripDto>;

public class GetSharedTripHandler(IApplicationDbContext db)
    : IRequestHandler<GetSharedTripQuery, SharedTripDto>
{
    public async Task<SharedTripDto> Handle(GetSharedTripQuery req, CancellationToken ct)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.ShareToken == req.Token &&
                                      t.ShareTokenExpiresAt > DateTime.UtcNow, ct)
            ?? throw new KeyNotFoundException("Shared trip not found or link has expired.");

        string? driverFirstName = null;
        string? plate = null;

        if (trip.DriverId != null)
        {
            var dp = await db.DriverProfiles
                .Include(d => d.User)
                .Include(d => d.Vehicles.Where(v => v.IsActive))
                .FirstOrDefaultAsync(d => d.UserId == trip.DriverId, ct);

            driverFirstName = dp?.User.FirstName;
            plate           = dp?.Vehicles.FirstOrDefault()?.Plate;
        }

        return new SharedTripDto(
            Code:              trip.Code,
            Status:            RideProjector.ToRiderStatus(trip.JobState),
            PickupLabel:       trip.PickupLabel,
            DropoffLabel:      trip.DropoffLabel,
            DriverFirstName:   driverFirstName,
            VehiclePlate:      plate,
            EncodedPolyline:   trip.EncodedPolyline,
            EstimatedArrival:  null);
    }
}
