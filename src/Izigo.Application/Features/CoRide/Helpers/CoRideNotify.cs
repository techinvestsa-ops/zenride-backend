using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.CoRide.Dtos;
using Izigo.Application.Features.CoRide.Queries;
using Izigo.Application.Features.Realtime.Dtos;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.CoRide.Helpers;

internal static class CoRideNotify
{
    public static async Task PublishBookingUpdatedAsync(
        IRealtimeService realtime,
        IPushService push,
        IApplicationDbContext db,
        CoRideBooking booking,
        CoRideListing listing,
        CancellationToken ct)
    {
        var payload = new CoRideBookingUpdatedEvent(
            booking.Id,
            booking.Status.ToString().ToLower(),
            listing.DepartureAt);

        await realtime.PublishToUserAsync(booking.RiderId, "coride.booking_updated", payload, ct);

        var token = await LatestFcmTokenAsync(db, booking.RiderId, ct);
        if (token != null)
        {
            var (title, body) = booking.Status switch
            {
                CoRideBookingStatus.DriverArriving => ("Driver is on the way", "Your co-ride is departing soon"),
                CoRideBookingStatus.InRide          => ("Co-ride started", "You are now on your shared ride"),
                CoRideBookingStatus.Completed       => ("Co-ride completed", "Thanks for riding with Izigo"),
                CoRideBookingStatus.Cancelled       => ("Co-ride cancelled", listing.CancellationReason ?? "This trip was cancelled"),
                _                                   => ("Co-ride update", $"Status: {booking.Status.ToString().ToLower()}"),
            };

            await push.SendAsync(
                token, title, body,
                type: "coride.booking_updated",
                entityId: booking.Id,
                deepLink: $"izigo://co-ride/booking/{booking.Id}",
                ct: ct);
        }
    }

    public static async Task PublishRequestMatchedAsync(
        IRealtimeService realtime,
        IPushService push,
        IApplicationDbContext db,
        CoRideRequest request,
        CoRideListingDto listing,
        CancellationToken ct)
    {
        await realtime.PublishToUserAsync(request.RiderId, "coride.request_matched",
            new CoRideRequestMatchedEvent(request.Id, listing), ct);

        var token = await LatestFcmTokenAsync(db, request.RiderId, ct);
        if (token != null)
            await push.SendAsync(
                token,
                title: "Co-ride match found",
                body:  $"{listing.Driver.Name} is heading to {listing.To.Label}",
                type: "coride.request_matched",
                entityId: request.Id,
                deepLink: $"izigo://co-ride/listing/{listing.Id}",
                ct: ct);
    }

    public static async Task NotifyListingBookingsAsync(
        IRealtimeService realtime,
        IPushService push,
        IApplicationDbContext db,
        CoRideListing listing,
        CoRideBookingStatus? onlyStatus,
        CancellationToken ct)
    {
        var bookings = listing.Bookings
            .Where(b => b.Status != CoRideBookingStatus.Cancelled &&
                        (onlyStatus == null || b.Status == onlyStatus))
            .ToList();

        foreach (var booking in bookings)
            await PublishBookingUpdatedAsync(realtime, push, db, booking, listing, ct);
    }

    private static Task<string?> LatestFcmTokenAsync(
        IApplicationDbContext db, string userId, CancellationToken ct)
        => db.UserDevices
            .Where(d => d.UserId == userId && d.FcmToken != null)
            .OrderByDescending(d => d.UpdatedAt)
            .Select(d => d.FcmToken!)
            .FirstOrDefaultAsync(ct);
}
