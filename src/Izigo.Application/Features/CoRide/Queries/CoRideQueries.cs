using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.CoRide.Dtos;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.CoRide.Queries;

// ── GET /co-ride/listings ─────────────────────────────────────────────────────

public record SearchListingsQuery(string UserId, SearchListingsRequest Req)
    : IRequest<List<CoRideListingDto>>;

public class SearchListingsHandler(IApplicationDbContext db)
    : IRequestHandler<SearchListingsQuery, List<CoRideListingDto>>
{
    public async Task<List<CoRideListingDto>> Handle(SearchListingsQuery q, CancellationToken ct)
    {
        var r = q.Req;
        var now = DateTime.UtcNow;
        var degApprox = (r.RadiusM ?? 5000) / 111_000.0;

        var query = db.CoRideListings
            .Where(l => l.Status == "open" &&
                        l.DepartureAt > now &&
                        l.SeatsLeft >= r.Seats &&
                        (double)l.FromLat >= r.FromLat - degApprox &&
                        (double)l.FromLat <= r.FromLat + degApprox &&
                        (double)l.FromLng >= r.FromLng - degApprox &&
                        (double)l.FromLng <= r.FromLng + degApprox);

        if (r.DepartureFrom.HasValue) query = query.Where(l => l.DepartureAt >= r.DepartureFrom.Value);
        if (r.DepartureTo.HasValue)   query = query.Where(l => l.DepartureAt <= r.DepartureTo.Value);
        if (r.EcoOnly)                query = query.Where(l => l.IsEco);

        query = r.Sort switch
        {
            "price"  => query.OrderBy(l => l.PricePerSeat),
            "rating" => query.OrderByDescending(l => l.DriverId),  // will sort by rating after join
            _        => query.OrderBy(l => l.DepartureAt)
        };

        var listings = await query
            .Include(l => l.Bookings)
            .Take(50)
            .ToListAsync(ct);

        var driverIds = listings.Select(l => l.DriverId).Distinct().ToList();
        var drivers = await db.DriverProfiles
            .Include(d => d.User)
            .Include(d => d.Vehicles.Where(v => v.IsActive))
            .Where(d => driverIds.Contains(d.UserId))
            .ToDictionaryAsync(d => d.UserId, ct);

        return listings.Select(l =>
        {
            drivers.TryGetValue(l.DriverId, out var dp);
            return MapListing(l, dp);
        }).ToList();
    }

    internal static CoRideListingDto MapListing(
        Domain.Entities.CoRideListing l,
        Domain.Entities.DriverProfile? dp)
    {
        var v = dp?.Vehicles.FirstOrDefault();
        return new CoRideListingDto(
            Id: l.Id,
            Driver: new CoRideDriverDto(
                Id: l.DriverId,
                Name: dp?.User?.FullName ?? "Driver",
                Rating: dp?.User != null ? (double)dp.User.Rating : 5.0,
                VehicleDescription: v != null ? $"{v.Make} {v.Model}".Trim() : "",
                Plate: v?.Plate ?? "",
                IsEco: l.IsEco,
                Badges: []),
            From: new CoRideLocationDto(l.FromLabel, (double)l.FromLat, (double)l.FromLng),
            To:   new CoRideLocationDto(l.ToLabel,   (double)l.ToLat,   (double)l.ToLng),
            DepartureAt: l.DepartureAt,
            MinutesToDeparture: (int)Math.Max(0, (l.DepartureAt - DateTime.UtcNow).TotalMinutes),
            SeatsTotal: l.SeatsTotal,
            SeatsTaken: l.SeatsTaken,
            SeatsLeft: l.SeatsLeft,
            PricePerSeat: l.PricePerSeat,
            ServiceFee: l.ServiceFee,
            Currency: l.Currency,
            RouteOverlapPct: 0,    // calculated by route comparison algorithm — 0 until implemented
            FromRequestId: null);
    }
}

// ── GET /co-ride/listings/{id} ────────────────────────────────────────────────

public record GetListingQuery(string UserId, string ListingId) : IRequest<CoRideListingDto>;

public class GetListingHandler(IApplicationDbContext db)
    : IRequestHandler<GetListingQuery, CoRideListingDto>
{
    public async Task<CoRideListingDto> Handle(GetListingQuery req, CancellationToken ct)
    {
        var listing = await db.CoRideListings
            .Include(l => l.Bookings)
            .FirstOrDefaultAsync(l => l.Id == req.ListingId, ct)
            ?? throw new KeyNotFoundException("Listing not found.");

        var dp = await db.DriverProfiles
            .Include(d => d.User)
            .Include(d => d.Vehicles.Where(v => v.IsActive))
            .FirstOrDefaultAsync(d => d.UserId == listing.DriverId, ct);

        return SearchListingsHandler.MapListing(listing, dp);
    }
}

// ── GET /co-ride/requests — my open match requests ────────────────────────────

public record GetMyRequestsQuery(string UserId) : IRequest<List<CoRideRequestDto>>;

public class GetMyRequestsHandler(IApplicationDbContext db)
    : IRequestHandler<GetMyRequestsQuery, List<CoRideRequestDto>>
{
    public async Task<List<CoRideRequestDto>> Handle(GetMyRequestsQuery req, CancellationToken ct)
        => await db.CoRideRequests
            .Where(r => r.RiderId == req.UserId &&
                        r.Status == CoRideRequestStatus.Searching)
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new CoRideRequestDto(
                r.Id, r.Status.ToString().ToLower(), r.SeatsNeeded,
                new CoRideLocationDto(r.PickupLabel, (double)r.PickupLat, (double)r.PickupLng),
                new CoRideLocationDto(r.DropoffLabel, (double)r.DropoffLat, (double)r.DropoffLng),
                r.DepartureWindowFrom, r.DepartureWindowTo))
            .ToListAsync(ct);
}

// ── GET /co-ride/bookings ─────────────────────────────────────────────────────

public record GetMyBookingsQuery(string UserId, string State) : IRequest<List<CoRideBookingDto>>;

public class GetMyBookingsHandler(IApplicationDbContext db)
    : IRequestHandler<GetMyBookingsQuery, List<CoRideBookingDto>>
{
    public async Task<List<CoRideBookingDto>> Handle(GetMyBookingsQuery req, CancellationToken ct)
    {
        var activeStatuses = new[] { CoRideBookingStatus.Upcoming, CoRideBookingStatus.DriverArriving, CoRideBookingStatus.InRide };
        var pastStatuses   = new[] { CoRideBookingStatus.Completed, CoRideBookingStatus.Cancelled };

        var statuses = req.State == "past" ? pastStatuses : activeStatuses;

        var bookings = await db.CoRideBookings
            .Include(b => b.Listing)
            .Where(b => b.RiderId == req.UserId && statuses.Contains(b.Status))
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(ct);

        var driverIds = bookings.Select(b => b.Listing.DriverId).Distinct().ToList();
        var drivers = await db.DriverProfiles
            .Include(d => d.User)
            .Include(d => d.Vehicles.Where(v => v.IsActive))
            .Where(d => driverIds.Contains(d.UserId))
            .ToDictionaryAsync(d => d.UserId, ct);

        return bookings.Select(b =>
        {
            drivers.TryGetValue(b.Listing.DriverId, out var dp);
            return new CoRideBookingDto(
                Id: b.Id,
                ListingId: b.ListingId,
                Listing: SearchListingsHandler.MapListing(b.Listing, dp),
                Seats: b.Seats,
                SeatLabels: System.Text.Json.JsonSerializer.Deserialize<string[]>(b.SeatLabelsJson) ?? [],
                PricePerSeat: b.PricePerSeat,
                ServiceFee: b.ServiceFee,
                PromoDiscount: b.PromoDiscount,
                Total: b.Total,
                Currency: b.Currency,
                Status: b.Status.ToString().ToLower(),
                PaymentMethod: b.PaymentMethod.ToString().ToLower());
        }).ToList();
    }
}

// ── GET /co-ride/bookings/{id} ────────────────────────────────────────────────

public record GetBookingQuery(string UserId, string BookingId) : IRequest<CoRideBookingDto>;

public class GetBookingHandler(IApplicationDbContext db)
    : IRequestHandler<GetBookingQuery, CoRideBookingDto>
{
    public async Task<CoRideBookingDto> Handle(GetBookingQuery req, CancellationToken ct)
    {
        var booking = await db.CoRideBookings
            .Include(b => b.Listing)
            .FirstOrDefaultAsync(b => b.Id == req.BookingId && b.RiderId == req.UserId, ct)
            ?? throw new KeyNotFoundException("Booking not found.");

        var dp = await db.DriverProfiles
            .Include(d => d.User)
            .Include(d => d.Vehicles.Where(v => v.IsActive))
            .FirstOrDefaultAsync(d => d.UserId == booking.Listing.DriverId, ct);

        return new CoRideBookingDto(
            Id: booking.Id,
            ListingId: booking.ListingId,
            Listing: SearchListingsHandler.MapListing(booking.Listing, dp),
            Seats: booking.Seats,
            SeatLabels: System.Text.Json.JsonSerializer.Deserialize<string[]>(booking.SeatLabelsJson) ?? [],
            PricePerSeat: booking.PricePerSeat,
            ServiceFee: booking.ServiceFee,
            PromoDiscount: booking.PromoDiscount,
            Total: booking.Total,
            Currency: booking.Currency,
            Status: booking.Status.ToString().ToLower(),
            PaymentMethod: booking.PaymentMethod.ToString().ToLower());
    }
}

// ── GET /driver/co-ride/listings ──────────────────────────────────────────────

public record GetDriverListingsQuery(string DriverId) : IRequest<List<CoRideListingDto>>;

public class GetDriverListingsHandler(IApplicationDbContext db)
    : IRequestHandler<GetDriverListingsQuery, List<CoRideListingDto>>
{
    public async Task<List<CoRideListingDto>> Handle(GetDriverListingsQuery req, CancellationToken ct)
    {
        var listings = await db.CoRideListings
            .Include(l => l.Bookings)
            .Where(l => l.DriverId == req.DriverId)
            .OrderByDescending(l => l.DepartureAt)
            .ToListAsync(ct);

        var dp = await db.DriverProfiles
            .Include(d => d.User)
            .Include(d => d.Vehicles.Where(v => v.IsActive))
            .FirstOrDefaultAsync(d => d.UserId == req.DriverId, ct);

        return listings.Select(l => SearchListingsHandler.MapListing(l, dp)).ToList();
    }
}

// ── GET /driver/co-ride/listings/{id}/manifest ────────────────────────────────

public record GetManifestQuery(string DriverId, string ListingId)
    : IRequest<List<PassengerManifestDto>>;

public class GetManifestHandler(IApplicationDbContext db)
    : IRequestHandler<GetManifestQuery, List<PassengerManifestDto>>
{
    public async Task<List<PassengerManifestDto>> Handle(GetManifestQuery req, CancellationToken ct)
    {
        var listing = await db.CoRideListings
            .FirstOrDefaultAsync(l => l.Id == req.ListingId && l.DriverId == req.DriverId, ct)
            ?? throw new KeyNotFoundException("Listing not found.");

        var bookings = await db.CoRideBookings
            .Where(b => b.ListingId == req.ListingId && b.Status != CoRideBookingStatus.Cancelled)
            .ToListAsync(ct);

        var riderIds = bookings.Select(b => b.RiderId).Distinct().ToList();
        var riders = await db.Users
            .Where(u => riderIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        return bookings.Select(b =>
        {
            riders.TryGetValue(b.RiderId, out var rider);
            return new PassengerManifestDto(
                BookingId: b.Id,
                PassengerName: rider?.FullName ?? "Passenger",
                SeatLabels: System.Text.Json.JsonSerializer.Deserialize<string[]>(b.SeatLabelsJson) ?? [],
                PaymentStatus: b.PaymentMethod == PaymentMethod.Cash ? "pending" : "paid",
                IsBoarded: b.Status == CoRideBookingStatus.InRide);
        }).ToList();
    }
}
