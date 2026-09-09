using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Driver.Dtos;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Driver.Queries;

// ── GET /driver/performance — spec: ?period=week|month, each metric has delta + target ──

public record GetPerformanceQuery(string DriverId, string Period) : IRequest<DriverPerformanceDto>;

public class GetPerformanceHandler(IApplicationDbContext db)
    : IRequestHandler<GetPerformanceQuery, DriverPerformanceDto>
{
    public async Task<DriverPerformanceDto> Handle(GetPerformanceQuery req, CancellationToken ct)
    {
        var dp = await db.DriverProfiles
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.UserId == req.DriverId, ct)
            ?? throw new KeyNotFoundException("Driver not found.");

        var acceptancePct   = Math.Round((double)dp.AcceptanceRate   * 100, 1);
        var cancellationPct = Math.Round((double)dp.CancellationRate  * 100, 1);
        var completionPct   = Math.Round((double)dp.CompletionRate    * 100, 1);
        var rating          = Math.Round((double)dp.User.Rating, 2);

        // Avg trip duration from completed trips in period
        var from = req.Period == "month" ? DateTime.UtcNow.AddDays(-30) : DateTime.UtcNow.AddDays(-7);
        var trips = await db.Trips
            .Where(t => t.DriverId == req.DriverId &&
                        t.JobState == Domain.Enums.JobState.Completed &&
                        t.CompletedAt >= from &&
                        t.StartedAt != null)
            .Select(t => new { t.StartedAt, t.CompletedAt })
            .ToListAsync(ct);

        var avgMin = trips.Count > 0
            ? trips.Average(t => (t.CompletedAt!.Value - t.StartedAt!.Value).TotalMinutes)
            : 0.0;

        var level = rating switch
        {
            >= 4.8 => "platinum",
            >= 4.5 => "gold",
            >= 4.0 => "silver",
            _      => "bronze"
        };

        return new DriverPerformanceDto(
            CustomerRating:   new(rating,          DeltaVsPrevious: null, Target: 4.8),
            AcceptanceRate:   new(acceptancePct,   DeltaVsPrevious: null, Target: 85.0),
            CancellationRate: new(cancellationPct, DeltaVsPrevious: null, Target: 5.0),
            CompletionRate:   new(completionPct,   DeltaVsPrevious: null, Target: 95.0),
            AvgTripMinutes:   new(Math.Round(avgMin, 1), DeltaVsPrevious: null, Target: null),
            Level:            level,
            Period:           req.Period);
    }
}

// ── GET /driver/insights/peak-hours ──────────────────────────────────────────

public record GetPeakHoursQuery(string DriverId) : IRequest<PeakHoursDto>;

public class GetPeakHoursHandler : IRequestHandler<GetPeakHoursQuery, PeakHoursDto>
{
    // Static peak-hours pattern for Abidjan — replace with real demand analytics
    private static readonly PeakSlotDto[] AbidjánPeaks =
    [
        new("monday",    7,  3), new("monday",    8,  3), new("monday",    17, 3), new("monday",    18, 3),
        new("tuesday",   7,  3), new("tuesday",   8,  3), new("tuesday",   17, 3), new("tuesday",   18, 3),
        new("wednesday", 7,  2), new("wednesday", 12, 2), new("wednesday", 17, 3),
        new("thursday",  7,  3), new("thursday",  17, 3), new("thursday",  18, 2),
        new("friday",    7,  3), new("friday",    8,  3), new("friday",    17, 3), new("friday",    18, 3), new("friday", 19, 2),
        new("saturday",  9,  2), new("saturday",  12, 2), new("saturday",  18, 2), new("saturday",  20, 3),
        new("sunday",    10, 1), new("sunday",    18, 2), new("sunday",    20, 2),
    ];

    public Task<PeakHoursDto> Handle(GetPeakHoursQuery req, CancellationToken ct)
        => Task.FromResult(new PeakHoursDto(AbidjánPeaks));
}

// ── GET /driver/badges ────────────────────────────────────────────────────────

public record GetBadgesQuery(string DriverId) : IRequest<List<BadgeDto>>;

public class GetBadgesHandler(IApplicationDbContext db)
    : IRequestHandler<GetBadgesQuery, List<BadgeDto>>
{
    public async Task<List<BadgeDto>> Handle(GetBadgesQuery req, CancellationToken ct)
    {
        var dp = await db.DriverProfiles
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.UserId == req.DriverId, ct)
            ?? throw new KeyNotFoundException("Driver not found.");

        var trips   = dp.TotalTripsCompleted;
        var rating  = (double)dp.User.Rating;

        return
        [
            new("first_trip",     "First Trip",          "Complete your first trip",          trips >= 1,   $"{Math.Min(trips, 1)}/1 trips"),
            new("ten_trips",      "10 Trips",            "Complete 10 trips",                 trips >= 10,  $"{Math.Min(trips, 10)}/10 trips"),
            new("fifty_trips",    "50 Trips",            "Complete 50 trips",                 trips >= 50,  $"{Math.Min(trips, 50)}/50 trips"),
            new("hundred_trips",  "Century Driver",      "Complete 100 trips",                trips >= 100, $"{Math.Min(trips, 100)}/100 trips"),
            new("five_star",      "5-Star Driver",       "Maintain a 5.0 rating for 20 trips",rating >= 4.9 && trips >= 20, $"{rating:F1} avg"),
            new("top_rated",      "Top Rated",           "Maintain 4.8+ rating for 50 trips", rating >= 4.8 && trips >= 50, $"{rating:F1} avg"),
            new("no_cancel",      "Zero Cancellations",  "Complete 20 trips without cancelling", dp.CancellationRate == 0 && trips >= 20, null),
            new("fast_accept",    "Quick Responder",     "Accept 95%+ of offers",             dp.AcceptanceRate >= 0.95m, $"{(double)dp.AcceptanceRate*100:F0}%"),
        ];
    }
}

// ── GET /driver/ratings ───────────────────────────────────────────────────────

public record GetDriverRatingsQuery(string DriverId, int Page, int PerPage)
    : IRequest<(List<DriverRatingItemDto> Items, int Total)>;

public class GetDriverRatingsHandler(IApplicationDbContext db)
    : IRequestHandler<GetDriverRatingsQuery, (List<DriverRatingItemDto>, int)>
{
    public async Task<(List<DriverRatingItemDto>, int)> Handle(
        GetDriverRatingsQuery req, CancellationToken ct)
    {
        var q = db.Trips
            .Where(t => t.DriverId == req.DriverId &&
                        t.RatingByRider.HasValue);

        var total = await q.CountAsync(ct);
        var raw = await q
            .OrderByDescending(t => t.CompletedAt)
            .Skip((req.Page - 1) * req.PerPage)
            .Take(req.PerPage)
            .Select(t => new { t.Id, t.Code, Rating = t.RatingByRider!.Value, At = t.CompletedAt ?? t.CreatedAt })
            .ToListAsync(ct);

        var items = raw.Select(t => new DriverRatingItemDto(t.Id, t.Code, t.Rating,
            Tags: null, Comment: null, RatedAt: t.At)).ToList();

        return (items, total);
    }
}
