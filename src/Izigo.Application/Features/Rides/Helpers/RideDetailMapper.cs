using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Rides.Dtos;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Rides.Helpers;

/// <summary>Builds the canonical GET /rides/{id} response shape from a Trip entity.</summary>
public static class RideDetailMapper
{
    public static async Task<RideDetailDto> BuildAsync(
        Trip trip, IApplicationDbContext db, CancellationToken ct,
        string shareBaseUrl = "https://izigo.app/t/",
        long cancellationFee = 500)
    {
        RideDriverDto? driverDto = null;

        if (trip.DriverId != null)
        {
            var dp = await db.DriverProfiles
                .Include(d => d.User)
                .Include(d => d.Vehicles.Where(v => v.IsActive))
                .FirstOrDefaultAsync(d => d.UserId == trip.DriverId, ct);

            if (dp != null)
            {
                var v = dp.Vehicles.FirstOrDefault();
                driverDto = new RideDriverDto(
                    Id: dp.UserId,
                    Name: $"{dp.User.FirstName} {dp.User.LastName[..1]}.",
                    Rating: (double)dp.User.Rating,
                    PhotoUrl: dp.User.PhotoUrl,
                    PhoneMasked: PhoneMasker.Mask(dp.User.Phone),
                    TripsCompleted: dp.TotalTripsCompleted,
                    Vehicle: v == null ? null : new RideVehicleDto(
                        $"{v.Make} {v.Model}".Trim(), v.Plate, v.Color));
            }
        }

        var isFinal = RideProjector.IsTerminal(trip.JobState);
        var status   = RideProjector.ToRiderStatus(trip.JobState);

        var actions = new RideActionsDto(
            CanCancel: !isFinal &&
                       trip.JobState != JobState.PickedUp &&
                       trip.JobState != JobState.EnRouteToDropoff,
            CancellationFee: cancellationFee,
            CanChangeDestination: trip.JobState is
                JobState.PickedUp or JobState.EnRouteToDropoff,
            CanChat: !isFinal && trip.DriverId != null,
            CanCall: !isFinal && trip.DriverId != null);

        return new RideDetailDto(
            TripId: trip.Id,
            Code: trip.Code,
            Vertical: trip.Vertical.ToString().ToLower(),
            Status: status,
            ClassCode: trip.ServiceClass.ToString().ToLower(),
            Pickup:  new RideLocationDto(trip.PickupLabel,  (double)trip.PickupLat,  (double)trip.PickupLng),
            Dropoff: new RideLocationDto(trip.DropoffLabel, (double)trip.DropoffLat, (double)trip.DropoffLng),
            StartOtp: trip.JobState == JobState.ArrivedAtPickup ? trip.StartOtp : null,
            Eta: new RideEtaDto(0, 0, null),
            EncodedPolyline: trip.EncodedPolyline,
            Progress: 0.0,
            Driver: driverDto,
            Fare: new RideFareDto(
                trip.FareGross + trip.FareServiceFee - trip.FareDiscount,
                trip.FareBase, trip.FareDistance, trip.FareTime,
                trip.FareServiceFee, trip.FareDiscount,
                trip.FareTip, trip.Currency, isFinal),
            Payment: new RidePaymentDto(
                trip.PaymentMethod.ToString().ToLower(), "authorized"),
            Timestamps: new RideTimestampsDto(
                trip.CreatedAt, trip.AssignedAt, trip.ArrivedAt,
                trip.StartedAt, trip.CompletedAt),
            Actions: actions,
            ShareUrl: trip.ShareToken != null
                ? $"{shareBaseUrl.TrimEnd('/')}/{trip.ShareToken}"
                : null);
    }
}
