namespace Izigo.Application.Features.Geo.Dtos;

// ── GET /geo/autocomplete ──────────────────────────────────────────────────────
// Exact field names from the contract
public record AutocompleteResultDto(
    string PlaceId,
    string PrimaryText,
    string SecondaryText,
    int? DistanceM
);

// ── GET /geo/place/{place_id} ─────────────────────────────────────────────────
public record PlaceDetailDto(
    string Name,
    string FormattedAddress,
    double Lat,
    double Lng
);

// ── GET /geo/reverse-geocode ──────────────────────────────────────────────────
public record ReverseGeocodeDto(
    string FormattedAddress,
    double Lat,
    double Lng
);

// ── GET /geo/geocode ──────────────────────────────────────────────────────────
public record GeocodeDto(
    string FormattedAddress,
    double Lat,
    double Lng,
    string? PlaceId
);

// ── GET /geo/route ────────────────────────────────────────────────────────────
// Returns exactly the fields the contract specifies
public record RouteDto(
    string EncodedPolyline,
    int DistanceM,
    int DurationS,
    int? DurationInTrafficS
);

// ── GET /geo/eta ──────────────────────────────────────────────────────────────
public record EtaResultDto(
    double DestLat,
    double DestLng,
    int? DurationS,
    int? DistanceM,
    string Status    // OK | NOT_FOUND | ZERO_RESULTS
);

// ── GET /geo/nearby-drivers ───────────────────────────────────────────────────
// Contract: "Return fuzzed positions and no identities"
public record NearbyDriverDto(
    double Lat,
    double Lng,
    string VehicleType    // bike | car | van
    // Deliberately NO id, NO name, NO plate
);
