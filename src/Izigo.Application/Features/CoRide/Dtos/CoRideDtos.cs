namespace Izigo.Application.Features.CoRide.Dtos;

// ── Listing — exact shape from the contract code block ───────────────────────

public record CoRideListingDto(
    string Id,
    CoRideDriverDto Driver,
    CoRideLocationDto From,
    CoRideLocationDto To,
    DateTime DepartureAt,
    int MinutesToDeparture,
    int SeatsTotal,
    int SeatsTaken,
    int SeatsLeft,
    long PricePerSeat,
    long ServiceFee,
    string Currency,
    int RouteOverlapPct,
    string? FromRequestId
);

public record CoRideDriverDto(
    string Id,
    string Name,
    double Rating,
    string VehicleDescription,   // "Toyota Corolla"
    string Plate,
    bool IsEco,
    string[] Badges
);

public record CoRideLocationDto(string Label, double Lat, double Lng);

// ── Booking ───────────────────────────────────────────────────────────────────

public record CoRideBookingDto(
    string Id,
    string ListingId,
    CoRideListingDto Listing,
    int Seats,
    string[] SeatLabels,
    long PricePerSeat,
    long ServiceFee,
    long PromoDiscount,
    long Total,
    string Currency,
    string Status,
    string PaymentMethod
);

// ── Match request ─────────────────────────────────────────────────────────────

public record CoRideRequestDto(
    string RequestId,
    string Status,
    int SeatsNeeded,
    CoRideLocationDto Pickup,
    CoRideLocationDto Dropoff,
    DateTime DepartureWindowFrom,
    DateTime DepartureWindowTo
);

// ── Requests ──────────────────────────────────────────────────────────────────

public record SearchListingsRequest(
    double FromLat, double FromLng,
    double ToLat, double ToLng,
    int? RadiusM,
    DateTime? DepartureFrom,
    DateTime? DepartureTo,
    int Seats,
    string Sort,        // departure | price | rating
    bool EcoOnly
);

public record CreateMatchRequest(
    double PickupLat, double PickupLng, string PickupLabel,
    double DropoffLat, double DropoffLng, string DropoffLabel,
    int SeatsNeeded,
    DateTime DepartureWindowFrom,
    DateTime DepartureWindowTo
);

public record BookSeatsRequest(
    string ListingId,
    int Seats,
    string PaymentMethod,
    string? PromoCode
);

public record RateCoRideRequest(int Stars, string[]? Tags, string? Comment);

public record CancelCoRideRequest(string? Reason);

// ── Driver requests ───────────────────────────────────────────────────────────

public record PublishListingRequest(
    double FromLat, double FromLng, string FromLabel,
    double ToLat, double ToLng, string ToLabel,
    DateTime DepartureAt,
    int SeatsTotal,
    long PricePerSeat,
    bool IsEco,
    bool? IsRecurring
);

public record EditListingRequest(DateTime? DepartureAt);

public record DeleteListingRequest(string? Reason);

// ── Driver manifest ───────────────────────────────────────────────────────────

public record PassengerManifestDto(
    string BookingId,
    string PassengerName,
    string[] SeatLabels,
    string PaymentStatus,
    bool IsBoarded
);
