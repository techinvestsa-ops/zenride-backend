using Izigo.Application.Common.Helpers;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Settings;
using Izigo.Application.Features.CoRide.Dtos;
using Izigo.Application.Features.CoRide.Helpers;
using Izigo.Application.Features.CoRide.Queries;
using Izigo.Domain.Common;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Izigo.Application.Features.CoRide.Commands;

// ── POST /co-ride/requests ────────────────────────────────────────────────────

public record CreateMatchRequestCommand(string RiderId, CreateMatchRequest Request)
    : IRequest<CoRideRequestDto>;

public class CreateMatchRequestHandler(IApplicationDbContext db)
    : IRequestHandler<CreateMatchRequestCommand, CoRideRequestDto>
{
    public async Task<CoRideRequestDto> Handle(CreateMatchRequestCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;

        // Cancel any active request for this rider before creating a new one
        var existing = await db.CoRideRequests
            .Where(r => r.RiderId == cmd.RiderId && r.Status == CoRideRequestStatus.Searching)
            .ToListAsync(ct);
        foreach (var e in existing)
            e.Status = CoRideRequestStatus.Cancelled;

        var request = new CoRideRequest
        {
            RiderId             = cmd.RiderId,
            PickupLat           = (decimal)req.PickupLat,
            PickupLng           = (decimal)req.PickupLng,
            PickupLabel         = req.PickupLabel,
            DropoffLat          = (decimal)req.DropoffLat,
            DropoffLng          = (decimal)req.DropoffLng,
            DropoffLabel        = req.DropoffLabel,
            SeatsNeeded         = req.SeatsNeeded,
            DepartureWindowFrom = req.DepartureWindowFrom,
            DepartureWindowTo   = req.DepartureWindowTo,
            Status              = CoRideRequestStatus.Searching,
            ExpiresAt           = req.DepartureWindowTo.AddHours(1),
        };

        db.CoRideRequests.Add(request);
        await db.SaveChangesAsync(ct);

        return new CoRideRequestDto(
            request.Id, request.Status.ToString().ToLower(), request.SeatsNeeded,
            new CoRideLocationDto(req.PickupLabel, req.PickupLat, req.PickupLng),
            new CoRideLocationDto(req.DropoffLabel, req.DropoffLat, req.DropoffLng),
            request.DepartureWindowFrom, request.DepartureWindowTo);
    }
}

// ── DELETE /co-ride/requests/{id} ─────────────────────────────────────────────

public record WithdrawRequestCommand(string RiderId, string RequestId) : IRequest;

public class WithdrawRequestHandler(IApplicationDbContext db)
    : IRequestHandler<WithdrawRequestCommand>
{
    public async Task Handle(WithdrawRequestCommand cmd, CancellationToken ct)
    {
        var request = await db.CoRideRequests
            .FirstOrDefaultAsync(r => r.Id == cmd.RequestId && r.RiderId == cmd.RiderId, ct)
            ?? throw new KeyNotFoundException("Request not found.");

        if (request.Status != CoRideRequestStatus.Searching)
            throw new InvalidOperationException("CONFLICT: Request is no longer active.");

        request.Status = CoRideRequestStatus.Cancelled;
        await db.SaveChangesAsync(ct);
    }
}

// ── POST /co-ride/bookings — seat inventory lock ───────────────────────────────

public record BookSeatsCommand(string RiderId, BookSeatsRequest Request, string? IdempotencyKey) : IRequest<CoRideBookingDto>;

public class BookSeatsHandler(IApplicationDbContext db, IRealtimeService realtime, IPushService push)
    : IRequestHandler<BookSeatsCommand, CoRideBookingDto>
{
    public async Task<CoRideBookingDto> Handle(BookSeatsCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;

        if (!Enum.TryParse<PaymentMethod>(req.PaymentMethod.Replace("_", ""), true, out var payMethod))
            throw new ArgumentException("VALIDATION_ERROR: Invalid payment_method.");

        // Pessimistic lock: re-read listing inside the same SaveChanges to avoid TOCTOU
        var listing = await db.CoRideListings
            .FirstOrDefaultAsync(l => l.Id == req.ListingId, ct)
            ?? throw new KeyNotFoundException("Listing not found.");

        if (listing.Status != "open")
            throw new InvalidOperationException("CONFLICT: LISTING_NOT_OPEN");

        if (listing.SeatsLeft < req.Seats)
            throw new InvalidOperationException("CONFLICT: SEATS_UNAVAILABLE");

        // Prevent double-booking by the same rider
        var alreadyBooked = await db.CoRideBookings
            .AnyAsync(b => b.ListingId == req.ListingId &&
                           b.RiderId == cmd.RiderId &&
                           b.Status != CoRideBookingStatus.Cancelled, ct);
        if (alreadyBooked)
            throw new InvalidOperationException("CONFLICT: ALREADY_BOOKED");

        long promoDiscount = 0;
        // TODO: apply promo code lookup here when promotions module is implemented

        var seatLabels = Enumerable.Range(listing.SeatsTaken + 1, req.Seats)
            .Select(i => $"S{i}")
            .ToArray();

        var total = (listing.PricePerSeat * req.Seats) + listing.ServiceFee - promoDiscount;

        if (payMethod == PaymentMethod.Wallet)
            await RiderWalletPayments.EnsureCanPayAsync(db, cmd.RiderId, total, ct);

        var takenCodes = await db.CoRideBookings
            .Where(b => b.ListingId == listing.Id && b.BoardingCode != null)
            .Select(b => b.BoardingCode!)
            .ToListAsync(ct);
        var boardingCode = CoRidePresence.NewBoardingCode(takenCodes);

        var booking = new CoRideBooking
        {
            ListingId      = listing.Id,
            BoardingCode   = boardingCode,
            RiderId        = cmd.RiderId,
            Seats          = req.Seats,
            SeatLabelsJson = System.Text.Json.JsonSerializer.Serialize(seatLabels),
            PricePerSeat   = listing.PricePerSeat,
            ServiceFee     = listing.ServiceFee,
            PromoDiscount  = promoDiscount,
            Total          = total,
            Currency       = listing.Currency,
            Status         = CoRideBookingStatus.Upcoming,
            PaymentMethod  = payMethod,
        };

        listing.SeatsTaken += req.Seats;
        if (listing.SeatsLeft == 0)
            listing.Status = "full";

        db.CoRideBookings.Add(booking);

        var matchedRequest = await db.CoRideRequests
            .FirstOrDefaultAsync(r => r.RiderId == cmd.RiderId &&
                                      r.Status == CoRideRequestStatus.Searching, ct);
        if (matchedRequest != null)
        {
            matchedRequest.Status           = CoRideRequestStatus.Matched;
            matchedRequest.MatchedListingId = listing.Id;
            matchedRequest.MatchedAt          = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        var dp = await db.DriverProfiles
            .Include(d => d.User)
            .Include(d => d.Vehicles.Where(v => v.IsActive))
            .FirstOrDefaultAsync(d => d.UserId == listing.DriverId, ct);

        var listingDto = SearchListingsHandler.MapListing(listing, dp);
        await CoRideNotify.PublishBookingUpdatedAsync(realtime, push, db, booking, listing, ct);
        await CoRideNotify.NotifyDriverOfBookingAsync(realtime, push, db, booking, listing, ct);

        if (matchedRequest != null)
            await CoRideNotify.PublishRequestMatchedAsync(realtime, push, db, matchedRequest, listingDto, ct);

        return new CoRideBookingDto(
            Id: booking.Id,
            ListingId: booking.ListingId,
            Listing: SearchListingsHandler.MapListing(listing, dp),
            Seats: booking.Seats,
            SeatLabels: seatLabels,
            PricePerSeat: booking.PricePerSeat,
            ServiceFee: booking.ServiceFee,
            PromoDiscount: booking.PromoDiscount,
            Total: booking.Total,
            Currency: booking.Currency,
            Status: booking.Status.ToString().ToLower(),
            BookedAt: booking.CreatedAt,
            PaymentMethod: booking.PaymentMethod.ToString().ToLower(),
            BoardingCode: booking.BoardingCode,
            BoardedAt: booking.BoardedAt,
            BoardLat: (double?)booking.BoardLat,
            BoardLng: (double?)booking.BoardLng,
            AlightedAt: booking.AlightedAt,
            AlightLat: (double?)booking.AlightLat,
            AlightLng: (double?)booking.AlightLng);
    }
}

// ── POST /co-ride/bookings/{id}/cancel ────────────────────────────────────────

public record CancelBookingCommand(string RiderId, string BookingId, string? Reason) : IRequest;

public class CancelBookingHandler(IApplicationDbContext db, IRealtimeService realtime, IPushService push)
    : IRequestHandler<CancelBookingCommand>
{
    public async Task Handle(CancelBookingCommand cmd, CancellationToken ct)
    {
        var booking = await db.CoRideBookings
            .Include(b => b.Listing)
            .FirstOrDefaultAsync(b => b.Id == cmd.BookingId && b.RiderId == cmd.RiderId, ct)
            ?? throw new KeyNotFoundException("Booking not found.");

        if (booking.Status == CoRideBookingStatus.Completed ||
            booking.Status == CoRideBookingStatus.Cancelled)
            throw new InvalidOperationException("CONFLICT: Booking is already finalised.");

        booking.Status = CoRideBookingStatus.Cancelled;
        booking.CancellationReason = cmd.Reason;

        // Release the seats back to the listing
        booking.Listing.SeatsTaken -= booking.Seats;
        if (booking.Listing.Status == "full")
            booking.Listing.Status = "open";

        await db.SaveChangesAsync(ct);
        await CoRideNotify.PublishBookingUpdatedAsync(
            realtime, push, db, booking, booking.Listing, ct);
        await CoRideNotify.NotifyDriverOfBookingAsync(
            realtime, push, db, booking, booking.Listing, ct);
    }
}

// ── POST /co-ride/bookings/{id}/rate ─────────────────────────────────────────

public record RateCoRideCommand(string RiderId, string BookingId, RateCoRideRequest Request) : IRequest;

public class RateCoRideHandler(IApplicationDbContext db)
    : IRequestHandler<RateCoRideCommand>
{
    public async Task Handle(RateCoRideCommand cmd, CancellationToken ct)
    {
        var booking = await db.CoRideBookings
            .FirstOrDefaultAsync(b => b.Id == cmd.BookingId && b.RiderId == cmd.RiderId, ct)
            ?? throw new KeyNotFoundException("Booking not found.");

        if (booking.Status != CoRideBookingStatus.Completed)
            throw new InvalidOperationException("CONFLICT: Can only rate completed bookings.");

        if (booking.RatingByRider.HasValue)
            throw new InvalidOperationException("CONFLICT: Already rated.");

        booking.RatingByRider = cmd.Request.Stars;
        await db.SaveChangesAsync(ct);
    }
}

// ── POST /driver/co-ride/listings ─────────────────────────────────────────────

public record PublishListingCommand(string DriverId, PublishListingRequest Request)
    : IRequest<CoRideListingDto>;

public class PublishListingHandler(
    IApplicationDbContext db,
    IOptions<AppSettings> appOptions,
    IRealtimeService realtime,
    IPushService push)
    : IRequestHandler<PublishListingCommand, CoRideListingDto>
{
    private const double MatchRadiusDeg = 5000 / 111_000.0;
    private static readonly TimeSpan MatchSlack = TimeSpan.FromMinutes(30);

    public async Task<CoRideListingDto> Handle(PublishListingCommand cmd, CancellationToken ct)
    {
        var req    = cmd.Request;
        var minMin = appOptions.Value.CoRideMinDepartureMinutes;

        if (req.DepartureAt <= DateTime.UtcNow.AddMinutes(minMin))
            throw new ArgumentException($"VALIDATION_ERROR: Departure must be at least {minMin} minutes from now.");

        var dp = await db.DriverProfiles
            .Include(d => d.User)
            .Include(d => d.Vehicles.Where(v => v.IsActive))
            .FirstOrDefaultAsync(d => d.UserId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Driver profile not found.");

        var listing = new CoRideListing
        {
            DriverId     = cmd.DriverId,
            FromLat      = (decimal)req.FromLat,
            FromLng      = (decimal)req.FromLng,
            FromLabel    = req.FromLabel,
            ToLat        = (decimal)req.ToLat,
            ToLng        = (decimal)req.ToLng,
            ToLabel      = req.ToLabel,
            DepartureAt  = req.DepartureAt,
            SeatsTotal   = req.SeatsTotal,
            PricePerSeat = req.PricePerSeat,
            ServiceFee   = (long)(req.PricePerSeat * 0.10m),  // 10% platform fee
            Currency     = "XOF",
            IsEco        = req.IsEco,
            IsRecurring  = req.IsRecurring ?? false,
            Status       = "open",
        };

        db.CoRideListings.Add(listing);
        await db.SaveChangesAsync(ct);

        var dto = SearchListingsHandler.MapListing(listing, dp);
        await NotifyWaitingRidersAsync(listing, dto, ct);
        return dto;
    }

    private async Task NotifyWaitingRidersAsync(
        CoRideListing listing, CoRideListingDto dto, CancellationToken ct)
    {
        var r = (decimal)MatchRadiusDeg;
        var earliest = listing.DepartureAt - MatchSlack;
        var latest   = listing.DepartureAt + MatchSlack;
        var now      = DateTime.UtcNow;

        var requests = await db.CoRideRequests
            .Where(q => q.Status == CoRideRequestStatus.Searching
                     && q.ExpiresAt > now
                     && q.RiderId != listing.DriverId
                     && q.SeatsNeeded <= listing.SeatsTotal
                     && q.DepartureWindowFrom <= latest
                     && q.DepartureWindowTo >= earliest
                     && q.PickupLat  >= listing.FromLat - r && q.PickupLat  <= listing.FromLat + r
                     && q.PickupLng  >= listing.FromLng - r && q.PickupLng  <= listing.FromLng + r
                     && q.DropoffLat >= listing.ToLat - r   && q.DropoffLat <= listing.ToLat + r
                     && q.DropoffLng >= listing.ToLng - r   && q.DropoffLng <= listing.ToLng + r)
            .OrderBy(q => q.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        foreach (var request in requests)
            await CoRideNotify.PublishRequestMatchedAsync(realtime, push, db, request, dto, ct);
    }
}

// ── PATCH /driver/co-ride/listings/{id} ───────────────────────────────────────

public record EditListingCommand(string DriverId, string ListingId, EditListingRequest Request)
    : IRequest<CoRideListingDto>;

public class EditListingHandler(IApplicationDbContext db)
    : IRequestHandler<EditListingCommand, CoRideListingDto>
{
    public async Task<CoRideListingDto> Handle(EditListingCommand cmd, CancellationToken ct)
    {
        var listing = await db.CoRideListings
            .FirstOrDefaultAsync(l => l.Id == cmd.ListingId && l.DriverId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Listing not found.");

        if (listing.Status != "open")
            throw new InvalidOperationException("CONFLICT: Cannot edit a closed or full listing.");

        if (cmd.Request.DepartureAt.HasValue)
            listing.DepartureAt = cmd.Request.DepartureAt.Value;

        await db.SaveChangesAsync(ct);

        var dp = await db.DriverProfiles
            .Include(d => d.User)
            .Include(d => d.Vehicles.Where(v => v.IsActive))
            .FirstOrDefaultAsync(d => d.UserId == cmd.DriverId, ct);

        return SearchListingsHandler.MapListing(listing, dp);
    }
}

// ── DELETE /driver/co-ride/listings/{id} ──────────────────────────────────────

public record DeleteListingCommand(string DriverId, string ListingId, string? Reason) : IRequest;

public class DeleteListingHandler(IApplicationDbContext db, IRealtimeService realtime, IPushService push)
    : IRequestHandler<DeleteListingCommand>
{
    public async Task Handle(DeleteListingCommand cmd, CancellationToken ct)
    {
        var listing = await db.CoRideListings
            .Include(l => l.Bookings.Where(b => b.Status != CoRideBookingStatus.Cancelled &&
                                                 b.Status != CoRideBookingStatus.Completed))
            .FirstOrDefaultAsync(l => l.Id == cmd.ListingId && l.DriverId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Listing not found.");

        if (listing.Status == "cancelled")
            throw new InvalidOperationException("CONFLICT: Listing is already cancelled.");

        listing.Status = "cancelled";
        listing.CancellationReason = cmd.Reason;

        // Cancel all pending bookings
        foreach (var b in listing.Bookings)
        {
            b.Status = CoRideBookingStatus.Cancelled;
            b.CancellationReason = cmd.Reason;
        }

        await db.SaveChangesAsync(ct);
        await CoRideNotify.NotifyListingBookingsAsync(
            realtime, push, db, listing, onlyStatus: CoRideBookingStatus.Cancelled, ct);
    }
}

// ── POST /driver/co-ride/bookings/{id}/board ──────────────────────────────────

public record BoardPassengerCommand(string DriverId, string BookingId, string Code) : IRequest;

public class BoardPassengerHandler(IApplicationDbContext db, IRealtimeService realtime, IPushService push)
    : IRequestHandler<BoardPassengerCommand>
{
    public async Task Handle(BoardPassengerCommand cmd, CancellationToken ct)
    {
        var booking = await db.CoRideBookings
            .Include(b => b.Listing)
            .FirstOrDefaultAsync(b => b.Id == cmd.BookingId &&
                                      b.Listing.DriverId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Booking not found.");

        if (booking.Status == CoRideBookingStatus.InRide)
            return;  // idempotent

        if (booking.Status != CoRideBookingStatus.Upcoming &&
            booking.Status != CoRideBookingStatus.DriverArriving)
            throw new InvalidOperationException("CONFLICT: Passenger cannot be boarded in current state.");

        var code = cmd.Code.Trim();
        if (booking.BoardingCode != null && code != booking.BoardingCode)
            throw new ArgumentException("VALIDATION_ERROR: Boarding code does not match.");

        var fix = await CoRidePresence.RequireFixAsync(db, cmd.DriverId, ct);
        booking.Status = CoRideBookingStatus.InRide;
        booking.BoardedAt = DateTime.UtcNow;
        booking.BoardLat = fix.Lat;
        booking.BoardLng = fix.Lng;
        await db.SaveChangesAsync(ct);
        await CoRideNotify.PublishBookingUpdatedAsync(
            realtime, push, db, booking, booking.Listing, ct);
    }
}

public record AlightPassengerCommand(string DriverId, string BookingId) : IRequest;

public class AlightPassengerHandler(IApplicationDbContext db, IRealtimeService realtime, IPushService push)
    : IRequestHandler<AlightPassengerCommand>
{
    public async Task Handle(AlightPassengerCommand cmd, CancellationToken ct)
    {
        var booking = await db.CoRideBookings
            .Include(b => b.Listing)
            .FirstOrDefaultAsync(b => b.Id == cmd.BookingId &&
                                      b.Listing.DriverId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Booking not found.");

        if (booking.Status == CoRideBookingStatus.Completed)
            return;

        if (booking.Status != CoRideBookingStatus.InRide)
            throw new InvalidOperationException("CONFLICT: Only a boarded passenger can be dropped off.");

        var fix = await CoRidePresence.RequireFixAsync(db, cmd.DriverId, ct);
        await CoRidePresence.AlightAsync(db, booking, booking.Listing, fix, ct);
        await CoRideNotify.PublishBookingUpdatedAsync(
            realtime, push, db, booking, booking.Listing, ct);
    }
}

// ── POST /driver/co-ride/listings/{id}/start ──────────────────────────────────

public record StartCoRideDepartureCommand(string DriverId, string ListingId) : IRequest;

public class StartCoRideDepartureHandler(
    IApplicationDbContext db, IRealtimeService realtime, IPushService push)
    : IRequestHandler<StartCoRideDepartureCommand>
{
    public async Task Handle(StartCoRideDepartureCommand cmd, CancellationToken ct)
    {
        var listing = await db.CoRideListings
            .Include(l => l.Bookings)
            .FirstOrDefaultAsync(l => l.Id == cmd.ListingId && l.DriverId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Listing not found.");

        if (listing.Status is "cancelled" or "completed")
            throw new InvalidOperationException("CONFLICT: Listing is not active.");

        foreach (var booking in listing.Bookings.Where(b =>
                     b.Status is CoRideBookingStatus.Upcoming or CoRideBookingStatus.DriverArriving))
            booking.Status = CoRideBookingStatus.DriverArriving;

        listing.Status = "departed";
        await db.SaveChangesAsync(ct);
        await CoRideNotify.NotifyListingBookingsAsync(
            realtime, push, db, listing, CoRideBookingStatus.DriverArriving, ct);
    }
}

// ── POST /driver/co-ride/listings/{id}/complete ───────────────────────────────

public record CompleteCoRideListingCommand(string DriverId, string ListingId) : IRequest;

public class CompleteCoRideListingHandler(
    IApplicationDbContext db, IRealtimeService realtime, IPushService push)
    : IRequestHandler<CompleteCoRideListingCommand>
{
    public async Task Handle(CompleteCoRideListingCommand cmd, CancellationToken ct)
    {
        var listing = await db.CoRideListings
            .Include(l => l.Bookings)
            .FirstOrDefaultAsync(l => l.Id == cmd.ListingId && l.DriverId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Listing not found.");

        if (listing.Status is "cancelled" or "completed")
            throw new InvalidOperationException("CONFLICT: Listing is already finalised.");

        var fix = await CoRidePresence.LatestFixAsync(db, cmd.DriverId, ct);
        foreach (var booking in listing.Bookings.Where(b => b.Status == CoRideBookingStatus.InRide))
            await CoRidePresence.AlightAsync(db, booking, listing, fix, ct);

        listing.Status = "completed";
        await db.SaveChangesAsync(ct);

        foreach (var booking in listing.Bookings.Where(b => b.Status == CoRideBookingStatus.Completed))
            await CoRideNotify.PublishBookingUpdatedAsync(realtime, push, db, booking, listing, ct);
    }
}
