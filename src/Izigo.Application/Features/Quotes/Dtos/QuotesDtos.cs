namespace Izigo.Application.Features.Quotes.Dtos;

// Exact shape from the contract code block
public record QuoteDto(
    string QuoteId,
    DateTime ExpiresAt,
    string Currency,
    QuoteLocationDto Pickup,
    QuoteLocationDto Dropoff,
    int DistanceM,
    int DurationS,
    string? EncodedPolyline,
    SurgeDto Surge,
    QuoteOptionDto[] Options,
    string[] PaymentMethods
);

public record QuoteLocationDto(
    string Label,
    double Lat,
    double Lng,
    string? PlaceId
);

public record SurgeDto(bool Active, double Multiplier, string? Reason);

public record QuoteOptionDto(
    string ClassCode,
    string Name,
    string Icon,
    int Seats,
    int EtaPickupMin,
    int DurationMin,
    bool Available,
    QuoteFareDto Fare
);

public record QuoteFareDto(
    long Total,
    long Base,
    long Distance,
    long Time,
    long ServiceFee,
    long Discount
);

// ── GET /fare-rules ───────────────────────────────────────────────────────────

public record FareRuleDto(
    string ClassCode,
    long Base,
    long PerKm,
    long PerMin,
    long Minimum,
    long WaitingPerMin,
    long CancellationFee
);

// Requests
public record CreateQuoteRequest(
    string Vertical,           // ride | co_ride | package
    QuoteLocationInput Pickup,
    QuoteLocationInput Dropoff,
    int? Seats,
    PackageInput? Package,
    DateTime? ScheduledAt,
    string? PromoCode
);

public record QuoteLocationInput(
    string Label,
    double Lat,
    double Lng,
    string? PlaceId
);

public record PackageInput(string Size, bool IsExpress, bool IsFragile);
