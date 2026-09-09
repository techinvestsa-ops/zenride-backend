namespace Izigo.Application.Features.Driver.Dtos;

// ── Module 19: Driver status ──────────────────────────────────────────────────

public record DriverStatusDto(
    bool IsOnline,
    string KycStatus,
    bool OnboardingComplete,
    string[] VerticalsAllowed,
    long PendingCashSettlement,
    bool CashCapBlocked         // true when cash owed exceeds cap
);

// ── Module 19: Driver home screen ─────────────────────────────────────────────

public record DriverHomeDto(
    bool IsOnline,
    string KycStatus,
    DriverTodaySummaryDto Today,
    JobOfferDto? ActiveJob      // cold-start resume
);

public record DriverTodaySummaryDto(
    int TripsCompleted,
    long EarningsTotal,
    int OnlineMinutes,
    string Currency
);

// ── Module 19: Demand heatmap ─────────────────────────────────────────────────

public record DemandHeatmapDto(HeatmapZoneDto[] Zones);

public record WaitingFeeDto(long AccruedFee, string Currency, int WaitingSeconds);

public record HeatmapZoneDto(double Lat, double Lng, int DemandLevel);   // 0-3

// ── Module 19 / 20: Job offer ─────────────────────────────────────────────────

public record JobOfferDto(
    string TripId,
    string Code,
    string Vertical,
    string ClassCode,
    JobLocationDto Pickup,
    JobLocationDto? Dropoff,
    int DistanceM,
    int PickupDistanceM,    // driver → pickup
    int DurationS,
    long FareGross,
    string Currency,
    string PaymentMethod,
    int OfferExpiresInSec,  // 15s window
    string? NoteToDriver
);

public record JobLocationDto(string Label, double Lat, double Lng);

// ── Module 20: Active / job detail (driver projection) ────────────────────────

public record JobDetailDto(
    string TripId,
    string Code,
    string Vertical,
    string JobState,        // canonical driver state
    string ClassCode,
    JobLocationDto Pickup,
    JobLocationDto? Dropoff,
    string? StartOtp,
    JobCustomerDto Customer,
    JobFareDto Fare,
    string PaymentMethod,
    string? NoteToDriver,
    JobTimestampsDto Timestamps,
    JobActionsDto Actions
);

public record JobCustomerDto(
    string Name,
    string PhoneMasked,
    double Rating,
    string? PhotoUrl,
    int TotalTrips
);

public record JobFareDto(
    long Total,
    long Base,
    long Distance,
    long Time,
    long ServiceFee,
    long Discount,
    long DriverEarnings,
    string Currency,
    bool IsFinal
);

public record JobTimestampsDto(
    DateTime? RequestedAt,
    DateTime? AssignedAt,
    DateTime? ArrivedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt
);

public record JobActionsDto(
    bool CanArrive,
    bool CanStart,
    bool CanComplete,
    bool CanCancel
);

// ── Module 20: Job history item ───────────────────────────────────────────────

public record JobHistoryItemDto(
    string TripId,
    string Code,
    string Vertical,
    string JobState,
    string ClassCode,
    string PickupLabel,
    string DropoffLabel,
    long DriverEarnings,
    string Currency,
    DateTime? CompletedAt,
    DateTime CreatedAt
);

// ── Requests ──────────────────────────────────────────────────────────────────

public record SetStatusRequest(bool Online, string? Vertical);

public record UpdateLocationRequest(
    double Lat, double Lng,
    double? Heading, double? Speed, double? Accuracy,
    string? JobId
);
