namespace Izigo.Application.Features.Rides.Dtos;

// ── GET /rides/{id} — exact shape from the contract ──────────────────────────

public record RideDetailDto(
    string TripId,
    string Code,
    string Vertical,
    string Status,          // rider-facing projection
    string ClassCode,
    RideLocationDto Pickup,
    RideLocationDto Dropoff,
    string? StartOtp,       // 4-digit OTP rider shows to driver
    RideEtaDto? Eta,
    string? EncodedPolyline,
    double Progress,        // 0.0 – 1.0
    RideDriverDto? Driver,
    RideFareDto Fare,
    RidePaymentDto Payment,
    RideTimestampsDto Timestamps,
    RideActionsDto Actions,
    string? ShareUrl
);

public record RideLocationDto(string Label, double Lat, double Lng);

public record RideEtaDto(int PickupMin, int DropoffMin, DateTime? ArrivesAt);

public record RideDriverDto(
    string Id,
    string Name,
    double Rating,
    string? PhotoUrl,
    string PhoneMasked,
    int TripsCompleted,
    RideVehicleDto? Vehicle
);

public record RideVehicleDto(string MakeModel, string Plate, string? Color);

public record RideFareDto(
    long Total,
    long Base,
    long Distance,
    long Time,
    long ServiceFee,
    long Discount,
    long Tip,
    string Currency,
    bool IsFinal
);

public record RidePaymentDto(string Method, string Status);

public record RideTimestampsDto(
    DateTime? RequestedAt,
    DateTime? AssignedAt,
    DateTime? ArrivedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt
);

public record RideActionsDto(
    bool CanCancel,
    long CancellationFee,
    bool CanChangeDestination,
    bool CanChat,
    bool CanCall
);

// ── POST /rides — request body ────────────────────────────────────────────────

public record CreateRideRequest(
    string QuoteId,
    string ClassCode,
    string PaymentMethod,
    string? PromoCode,
    string? Note,
    DateTime? ScheduledAt,
    ForSomeoneElseDto? ForSomeoneElse
);

public record ForSomeoneElseDto(string Name, string Phone);

// ── POST /rides/{id}/cancel ───────────────────────────────────────────────────

public record CancelRideRequest(string ReasonCode, string? Note);

public record CancelRideResult(long CancellationFee, bool Charged);

// ── PATCH /rides/{id}/destination ────────────────────────────────────────────

public record ChangeDestinationRequest(
    string Label, double Lat, double Lng, string? PlaceId
);

// ── POST /rides/{id}/rate ─────────────────────────────────────────────────────

public record RateRideRequest(int Stars, string[]? Tags, string? Comment, long? TipAmount);

// ── POST /rides/{id}/tip ──────────────────────────────────────────────────────

public record TipRideRequest(long Amount);

// ── GET /rides/{id}/driver-location ──────────────────────────────────────────

public record DriverLocationDto(
    double Lat, double Lng, double? Heading, int? EtaMin, DateTime UpdatedAt
);

// ── GET /rides/{id}/receipt ───────────────────────────────────────────────────

public record ReceiptDto(
    string TripId,
    string Code,
    RideFareDto Fare,
    string? PdfUrl
);

// ── GET /rides/cancellation-reasons ──────────────────────────────────────────

public record CancellationReasonDto(string Code, string Label);

// ── GET /rides (history) ─────────────────────────────────────────────────────

public record RideHistoryItemDto(
    string TripId,
    string Code,
    string Vertical,
    string Status,
    string ClassCode,
    string PickupLabel,
    string DropoffLabel,
    long FareTotal,
    string Currency,
    DateTime? CompletedAt,
    DateTime RequestedAt
);

// ── PATCH /rides/{id}/payment-method request ──────────────────────────────────
public record ChangePaymentMethodRequest(string Method);

// ── POST /rides/{id}/note request ────────────────────────────────────────────
public record AddNoteRequest(string? Text, string? AudioUrl);

// ── POST /rides/{id}/lost-item request ───────────────────────────────────────
public record ReportLostItemRequest(string Description);
