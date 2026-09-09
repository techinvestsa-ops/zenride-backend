namespace Izigo.Application.Features.Driver.Dtos;

// ── GET /driver/performance — spec: each metric has delta_vs_previous + target ──

public record DriverPerformanceDto(
    PerformanceMetricDto CustomerRating,
    PerformanceMetricDto AcceptanceRate,
    PerformanceMetricDto CancellationRate,
    PerformanceMetricDto CompletionRate,
    PerformanceMetricDto AvgTripMinutes,
    string Level,           // bronze | silver | gold | platinum
    string Period           // week | month
);

public record PerformanceMetricDto(
    double Value,
    double? DeltaVsPrevious,  // spec: delta_vs_previous
    double? Target
);

// ── GET /driver/insights/peak-hours ──────────────────────────────────────────

public record PeakHoursDto(PeakSlotDto[] Slots);

public record PeakSlotDto(string Day, int Hour, int DemandLevel);

// ── GET /driver/badges ────────────────────────────────────────────────────────

public record BadgeDto(
    string Code,
    string Label,
    string Description,
    bool Earned,
    string? Progress
);

// ── GET /driver/ratings ───────────────────────────────────────────────────────

public record DriverRatingItemDto(
    string TripId,
    string Code,
    int Stars,
    string[]? Tags,
    string? Comment,
    DateTime RatedAt
);
