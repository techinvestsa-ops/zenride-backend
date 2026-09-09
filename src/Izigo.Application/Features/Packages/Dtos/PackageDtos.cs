namespace Izigo.Application.Features.Packages.Dtos;

// ── Package detail — GET /packages/{id} ──────────────────────────────────────

public record PackageDto(
    string PackageId,
    string TrackingId,
    string Status,
    PackageLocationDto Pickup,
    PackageLocationDto Dropoff,
    PackageRecipientDto Recipient,
    string Size,          // small | large
    bool IsExpress,
    bool IsFragile,
    string? Description,
    bool RecipientPays,
    long FareTotal,
    string Currency,
    string PaymentMethod,
    PackageCourierDto? Courier,
    PackageActionsDto Actions,
    DateTime CreatedAt,
    DateTime? PickedUpAt,
    DateTime? DeliveredAt,
    DateTime? CancelledAt
);

public record PackageLocationDto(string Label, double Lat, double Lng);

public record PackageRecipientDto(string Name, string PhoneMasked);

public record PackageCourierDto(
    string Id,
    string Name,
    double Rating,
    string? PhotoUrl,
    string PhoneMasked,
    PackageVehicleDto? Vehicle
);

public record PackageVehicleDto(string MakeModel, string Plate, string? Color);

public record PackageActionsDto(bool CanCancel, bool CanRate);

// ── Public tracking — GET /packages/track/{trackingId} ───────────────────────

public record PackageTrackDto(
    string TrackingId,
    string Status,
    PackageLocationDto Pickup,
    PackageLocationDto Dropoff,
    string RecipientName,
    DateTime? EstimatedDelivery,
    DateTime? PickedUpAt,
    DateTime? DeliveredAt
);

// ── History item — GET /packages ──────────────────────────────────────────────

public record PackageHistoryItemDto(
    string PackageId,
    string TrackingId,
    string Status,
    string PickupLabel,
    string DropoffLabel,
    string RecipientName,
    long FareTotal,
    string Currency,
    DateTime? DeliveredAt,
    DateTime CreatedAt
);

// ── POST /packages — request body ────────────────────────────────────────────

public record CreatePackageRequest(
    string QuoteId,
    string ClassCode,             // package_small | package_large
    string PaymentMethod,
    string RecipientName,
    string RecipientPhone,
    bool RecipientPays,
    string? Description,
    string? Instructions,
    bool IsFragile,
    bool IsExpress,
    long? DeclaredValue
);

// ── POST /packages/{id}/cancel ────────────────────────────────────────────────

public record CancelPackageRequest(string? Reason);

// ── POST /packages/{id}/rate ──────────────────────────────────────────────────

public record RatePackageRequest(int Stars, string? Comment);

// ── Driver: POST /driver/packages/{id}/proof ──────────────────────────────────

public record ProofOfDeliveryDto(string ProofCode, string? PhotoUrl);
