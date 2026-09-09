namespace Izigo.Application.Features.Driver.Dtos;

// ── Module 23: Vehicles ───────────────────────────────────────────────────────

public record VehicleDto(
    string Id,
    string Make,
    string Model,
    int Year,
    string Color,
    string Plate,
    string Type,
    int Seats,
    bool IsActive,
    bool PendingReview
);

public record AddVehicleRequest(
    string Make,
    string Model,
    int Year,
    string Color,
    string Plate,
    string VehicleType,
    int Seats
);

public record UpdateVehicleRequest(
    string? Color,
    string? Plate,
    bool? IsActive
);

// ── Module 23: Documents ──────────────────────────────────────────────────────

public record DriverDocumentDto(
    string Id,
    string Type,
    string FileUrl,
    string? BackFileUrl,
    string Status,
    string? RejectionReason,
    DateTime? ExpiresAt,
    int? DaysToExpiry,      // spec: days_to_expiry
    DateTime UploadedAt
);

public record UploadDocumentRequest(
    string Type,
    string FileUrl,
    string? BackFileUrl,
    DateTime? ExpiresAt
);

// ── Module 23: Driver Preferences ────────────────────────────────────────────

public record DriverPreferencesDto(
    string[] Verticals,             // spec: verticals[] accepted
    double? MaxPickupDistanceKm,    // spec: max_pickup_distance_km
    bool AutoAccept,                // spec: auto_accept
    bool AcceptCash,
    bool AcceptWallet,
    bool AcceptCard,
    bool AcceptMobileMoney,
    bool NotifyOnNewOffer,
    bool SilentMode
);

public record UpdateDriverPreferencesRequest(
    string[]? Verticals,
    double? MaxPickupDistanceKm,
    bool? AutoAccept,
    bool? AcceptCash,
    bool? AcceptWallet,
    bool? AcceptCard,
    bool? AcceptMobileMoney,
    bool? NotifyOnNewOffer,
    bool? SilentMode
);
