using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Metrics.Queries;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record KpiMetricDto(decimal Value, decimal DeltaPct, string Note, decimal[] Sparkline);
public record KpisDto(KpiMetricDto Gmv, KpiMetricDto NetRevenue, KpiMetricDto CompletedTrips,
    KpiMetricDto DriversOnline, KpiMetricDto NewRiders, KpiMetricDto AvgFare);

public record SeriesPointDto(string Label, decimal Value);

public record VerticalMixPointDto(string Label, int Ride, int CoRide, int Package);

public record SupplyDemandPointDto(string Label, int Online, int Requests);

public record AlertDto(string Type, int Count, string Severity, string DeepLink);

public record FunnelStepDto(string Step, int Submitted, int Approved, decimal DropPct);
public record ConversionDto(int Total, int Converted, decimal Pct);
public record FunnelsDto(ConversionDto SignupToFirstTrip, ConversionDto RequestToCompleted,
    IEnumerable<FunnelStepDto> KycDropOff);

public record CohortWeekDto(int Week, decimal TripsPerUser);
public record CohortDto(string Label, int SignupCount, IEnumerable<CohortWeekDto> WeeksSince);

// ── GET /admin/metrics/kpis ───────────────────────────────────────────────────

public record GetKpisQuery(string Market, int Range) : IRequest<object>;

public class GetKpisHandler(IApplicationDbContext db) : IRequestHandler<GetKpisQuery, object>
{
    public async Task<object> Handle(GetKpisQuery req, CancellationToken ct)
    {
        var range        = req.Range is 1 or 7 or 30 or 90 ? req.Range : 7;
        var now          = DateTime.UtcNow.Date;
        var currentStart = now.AddDays(-range);
        var previousStart = now.AddDays(-range * 2);

        // Completed trips for current and previous period
        var completedCurrent = await db.Trips
            .Where(t => t.Market == req.Market
                     && t.JobState == JobState.Completed
                     && t.CompletedAt >= currentStart)
            .Select(t => new { t.FareGross, t.CommissionAmount, t.CompletedAt })
            .ToListAsync(ct);

        var completedPrevious = await db.Trips
            .Where(t => t.Market == req.Market
                     && t.JobState == JobState.Completed
                     && t.CompletedAt >= previousStart
                     && t.CompletedAt < currentStart)
            .Select(t => new { t.FareGross, t.CommissionAmount })
            .ToListAsync(ct);

        // GMV
        var gmvCurrent  = completedCurrent.Sum(t => (decimal)t.FareGross);
        var gmvPrevious = completedPrevious.Sum(t => (decimal)t.FareGross);
        var gmvSparkline = BuildDailySparkline(
            completedCurrent.Select(t => (t.CompletedAt!.Value.Date, (decimal)t.FareGross)), range, now);

        // Net revenue (commission)
        var revCurrent  = completedCurrent.Sum(t => (decimal)t.CommissionAmount);
        var revPrevious = completedPrevious.Sum(t => (decimal)t.CommissionAmount);
        var revSparkline = BuildDailySparkline(
            completedCurrent.Select(t => (t.CompletedAt!.Value.Date, (decimal)t.CommissionAmount)), range, now);

        // Completed trips count
        var tripsCurrent  = completedCurrent.Count;
        var tripsPrevious = completedPrevious.Count;
        var tripsSparkline = BuildDailyCountSparkline(
            completedCurrent.Select(t => t.CompletedAt!.Value.Date), range, now);

        // New riders
        var ridersCurrent = await db.Users
            .CountAsync(u => u.Role == UserRole.Rider && u.CreatedAt >= currentStart, ct);
        var ridersPrevious = await db.Users
            .CountAsync(u => u.Role == UserRole.Rider
                          && u.CreatedAt >= previousStart
                          && u.CreatedAt < currentStart, ct);
        var ridersSparkline = await BuildUserDailySparkline(db, req.Market, range, now, ct);

        // Drivers online (snapshot + sparkline from location pings)
        var driversOnlineNow = await db.DriverProfiles.CountAsync(d => d.IsOnline, ct);
        var driversPingsCurrent = await db.DriverLocationPoints
            .Where(p => p.RecordedAt >= currentStart)
            .Select(p => new { p.DriverId, Day = p.RecordedAt.Date })
            .Distinct()
            .GroupBy(p => p.Day)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var driversPingsPrevious = await db.DriverLocationPoints
            .Where(p => p.RecordedAt >= previousStart && p.RecordedAt < currentStart)
            .Select(p => p.DriverId).Distinct().CountAsync(ct);
        var driversCurrent  = driversPingsCurrent.Sum(g => g.Count);
        var driversSparkline = BuildDailyCountFromGroups(driversPingsCurrent
            .Select(g => (g.Day, (decimal)g.Count)), range, now);

        // Avg fare
        var avgFareCurrent  = completedCurrent.Count > 0
            ? completedCurrent.Average(t => (decimal)t.FareGross) : 0m;
        var avgFarePrevious = completedPrevious.Count > 0
            ? completedPrevious.Average(t => (decimal)t.FareGross) : 0m;
        var avgFareSparkline = BuildDailyAvgSparkline(
            completedCurrent.Select(t => (t.CompletedAt!.Value.Date, (decimal)t.FareGross)), range, now);

        return AdminApiResponse.Ok(new KpisDto(
            Gmv:            new KpiMetricDto(gmvCurrent,         Delta(gmvCurrent, gmvPrevious),         $"vs previous {range}d", gmvSparkline),
            NetRevenue:     new KpiMetricDto(revCurrent,         Delta(revCurrent, revPrevious),         $"commission earned",    revSparkline),
            CompletedTrips: new KpiMetricDto(tripsCurrent,       Delta(tripsCurrent, tripsPrevious),     $"trips completed",      tripsSparkline),
            DriversOnline:  new KpiMetricDto(driversOnlineNow,   Delta(driversCurrent, driversPingsPrevious), "currently online", driversSparkline),
            NewRiders:      new KpiMetricDto(ridersCurrent,      Delta(ridersCurrent, ridersPrevious),   $"new signups",          ridersSparkline),
            AvgFare:        new KpiMetricDto(avgFareCurrent,     Delta(avgFareCurrent, avgFarePrevious), "per completed trip",    avgFareSparkline)
        ));
    }

    private static decimal Delta(decimal current, decimal previous)
        => previous == 0 ? 0 : Math.Round((current - previous) / previous * 100, 1);

    private static decimal[] BuildDailySparkline(
        IEnumerable<(DateTime Date, decimal Value)> data, int range, DateTime now)
    {
        var byDay = data.GroupBy(x => x.Date)
                        .ToDictionary(g => g.Key, g => g.Sum(x => x.Value));
        return Enumerable.Range(0, range)
            .Select(i => byDay.GetValueOrDefault(now.AddDays(-(range - 1 - i)), 0m))
            .ToArray();
    }

    private static decimal[] BuildDailyCountSparkline(IEnumerable<DateTime> dates, int range, DateTime now)
    {
        var byDay = dates.GroupBy(d => d).ToDictionary(g => g.Key, g => (decimal)g.Count());
        return Enumerable.Range(0, range)
            .Select(i => byDay.GetValueOrDefault(now.AddDays(-(range - 1 - i)), 0m))
            .ToArray();
    }

    private static decimal[] BuildDailyCountFromGroups(IEnumerable<(DateTime Day, decimal Count)> groups,
        int range, DateTime now)
    {
        var byDay = groups.ToDictionary(g => g.Day, g => g.Count);
        return Enumerable.Range(0, range)
            .Select(i => byDay.GetValueOrDefault(now.AddDays(-(range - 1 - i)), 0m))
            .ToArray();
    }

    private static decimal[] BuildDailyAvgSparkline(
        IEnumerable<(DateTime Date, decimal Value)> data, int range, DateTime now)
    {
        var byDay = data.GroupBy(x => x.Date)
                        .ToDictionary(g => g.Key, g => g.Average(x => x.Value));
        return Enumerable.Range(0, range)
            .Select(i => byDay.GetValueOrDefault(now.AddDays(-(range - 1 - i)), 0m))
            .ToArray();
    }

    private static async Task<decimal[]> BuildUserDailySparkline(
        IApplicationDbContext db, string market, int range, DateTime now, CancellationToken ct)
    {
        var start = now.AddDays(-range);
        var byDay = await db.Users
            .Where(u => u.Role == UserRole.Rider && u.CreatedAt >= start)
            .GroupBy(u => u.CreatedAt.Date)
            .Select(g => new { Day = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var dict = byDay.ToDictionary(g => g.Day, g => (decimal)g.Count);
        return Enumerable.Range(0, range)
            .Select(i => dict.GetValueOrDefault(now.AddDays(-(range - 1 - i)), 0m))
            .ToArray();
    }
}

// ── GET /admin/metrics/gmv-series ─────────────────────────────────────────────

public record GetGmvSeriesQuery(string Market, int Days) : IRequest<object>;

public class GetGmvSeriesHandler(IApplicationDbContext db) : IRequestHandler<GetGmvSeriesQuery, object>
{
    public async Task<object> Handle(GetGmvSeriesQuery req, CancellationToken ct)
    {
        var days  = Math.Max(1, Math.Min(90, req.Days));
        var start = DateTime.UtcNow.Date.AddDays(-days);

        var raw = await db.Trips
            .Where(t => t.Market == req.Market
                     && t.JobState == JobState.Completed
                     && t.CompletedAt >= start)
            .GroupBy(t => t.CompletedAt!.Value.Date)
            .Select(g => new { Day = g.Key, Total = g.Sum(t => (long)t.FareGross) })
            .ToListAsync(ct);

        var byDay = raw.ToDictionary(g => g.Day, g => (decimal)g.Total);
        var series = Enumerable.Range(0, days).Select(i =>
        {
            var day = start.AddDays(i);
            return new SeriesPointDto(day.ToString("MMM d"), byDay.GetValueOrDefault(day, 0m));
        }).ToArray();

        return AdminApiResponse.Ok(series);
    }
}

// ── GET /admin/metrics/vertical-mix ──────────────────────────────────────────

public record GetVerticalMixQuery(string Market, int? Days) : IRequest<object>;

public class GetVerticalMixHandler(IApplicationDbContext db) : IRequestHandler<GetVerticalMixQuery, object>
{
    public async Task<object> Handle(GetVerticalMixQuery req, CancellationToken ct)
    {
        var days  = Math.Max(1, Math.Min(90, req.Days ?? 14));
        var start = DateTime.UtcNow.Date.AddDays(-days);

        var raw = await db.Trips
            .Where(t => t.Market == req.Market && t.CompletedAt >= start)
            .GroupBy(t => new { t.CompletedAt!.Value.Date, t.Vertical })
            .Select(g => new { g.Key.Date, g.Key.Vertical, Count = g.Count() })
            .ToListAsync(ct);

        var byDay = raw.GroupBy(r => r.Date).ToDictionary(g => g.Key, g => g.ToList());

        var series = Enumerable.Range(0, days).Select(i =>
        {
            var day    = start.AddDays(i);
            var groups = byDay.GetValueOrDefault(day, []);
            return new VerticalMixPointDto(
                Label:   day.ToString("MMM d"),
                Ride:    groups.FirstOrDefault(g => g.Vertical == Vertical.Ride)?.Count    ?? 0,
                CoRide:  groups.FirstOrDefault(g => g.Vertical == Vertical.CoRide)?.Count  ?? 0,
                Package: groups.FirstOrDefault(g => g.Vertical == Vertical.Package)?.Count ?? 0);
        }).ToArray();

        return AdminApiResponse.Ok(series);
    }
}

// ── GET /admin/metrics/supply-demand ─────────────────────────────────────────
// 24 hourly buckets for the current day

public record GetSupplyDemandQuery(string Market) : IRequest<object>;

public class GetSupplyDemandHandler(IApplicationDbContext db) : IRequestHandler<GetSupplyDemandQuery, object>
{
    public async Task<object> Handle(GetSupplyDemandQuery req, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        // Drivers with location pings per hour today (proxy for online count)
        var onlinePings = await db.DriverLocationPoints
            .Where(p => p.RecordedAt >= today && p.RecordedAt < tomorrow)
            .GroupBy(p => p.RecordedAt.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Select(p => p.DriverId).Distinct().Count() })
            .ToListAsync(ct);

        // Trip requests (created) per hour today
        var requests = await db.Trips
            .Where(t => t.Market == req.Market
                     && t.CreatedAt >= today
                     && t.CreatedAt < tomorrow)
            .GroupBy(t => t.CreatedAt.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var onlineByHour  = onlinePings.ToDictionary(g => g.Hour, g => g.Count);
        var requestByHour = requests.ToDictionary(g => g.Hour, g => g.Count);

        var buckets = Enumerable.Range(0, 24).Select(h =>
            new SupplyDemandPointDto(
                Label:    $"{h:D2}:00",
                Online:   onlineByHour.GetValueOrDefault(h, 0),
                Requests: requestByHour.GetValueOrDefault(h, 0)))
            .ToArray();

        return AdminApiResponse.Ok(buckets);
    }
}

// ── GET /admin/metrics/alerts ─────────────────────────────────────────────────

public record GetAlertsQuery(string Market) : IRequest<object>;

public class GetAlertsHandler(IApplicationDbContext db) : IRequestHandler<GetAlertsQuery, object>
{
    public async Task<object> Handle(GetAlertsQuery req, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var thirtyDays = now.AddDays(30);

        var kycBacklog = await db.DriverOnboardings
            .CountAsync(o => o.SubmittedAt != null && o.ReviewedAt == null, ct);

        var failedPayouts = await db.Payouts
            .CountAsync(p => p.Market == req.Market && p.Status == PaymentStatus.Failed, ct);

        var openSos = await db.SosIncidents
            .CountAsync(s => s.Market == req.Market && s.Status == SosStatus.Active, ct);

        var expiringDocs = await db.DriverDocuments
            .CountAsync(d => d.Status == OnboardingStepStatus.Approved
                          && d.ExpiresAt.HasValue
                          && d.ExpiresAt.Value >= now
                          && d.ExpiresAt.Value <= thirtyDays, ct);

        var pastSla = await db.SupportTickets
            .CountAsync(t => t.Market == req.Market
                          && (t.Status == TicketStatus.Open || t.Status == TicketStatus.Pending)
                          && t.SlaDeadline.HasValue
                          && t.SlaDeadline.Value < now, ct);

        var alerts = new List<AlertDto>();

        if (kycBacklog > 0)
            alerts.Add(new AlertDto("kyc_backlog", kycBacklog,
                kycBacklog > 10 ? "critical" : "warning", "/kyc"));

        if (failedPayouts > 0)
            alerts.Add(new AlertDto("failed_payouts", failedPayouts, "critical", "/payouts?status=failed"));

        if (openSos > 0)
            alerts.Add(new AlertDto("open_sos", openSos, "critical", "/safety"));

        if (expiringDocs > 0)
            alerts.Add(new AlertDto("expiring_documents", expiringDocs,
                expiringDocs > 5 ? "warning" : "info", "/drivers?docs_expiring_within_days=30"));

        if (pastSla > 0)
            alerts.Add(new AlertDto("tickets_past_sla", pastSla, "warning", "/support?past_sla=true"));

        return AdminApiResponse.Ok(alerts);
    }
}

// ── GET /admin/metrics/funnels ────────────────────────────────────────────────

public record GetFunnelsQuery(string Market) : IRequest<object>;

public class GetFunnelsHandler(IApplicationDbContext db) : IRequestHandler<GetFunnelsQuery, object>
{
    public async Task<object> Handle(GetFunnelsQuery req, CancellationToken ct)
    {
        var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

        // Signup → first trip (riders who signed up in last 30 days)
        var recentRiderIds = await db.Users
            .Where(u => u.Role == UserRole.Rider && u.CreatedAt >= thirtyDaysAgo)
            .Select(u => u.Id)
            .ToListAsync(ct);

        var convertedRiders = recentRiderIds.Any()
            ? await db.Trips
                .Where(t => t.Market == req.Market
                         && recentRiderIds.Contains(t.RiderId)
                         && t.JobState == JobState.Completed)
                .Select(t => t.RiderId)
                .Distinct()
                .CountAsync(ct)
            : 0;

        // Request → completed (last 30 days)
        var totalRequests = await db.Trips
            .CountAsync(t => t.Market == req.Market && t.CreatedAt >= thirtyDaysAgo, ct);
        var completedRequests = await db.Trips
            .CountAsync(t => t.Market == req.Market
                          && t.CreatedAt >= thirtyDaysAgo
                          && t.JobState == JobState.Completed, ct);

        // KYC step drop-off
        var onboardings = await db.DriverOnboardings.ToListAsync(ct);
        var stepDropOff = new[]
        {
            BuildFunnelStep("personal",   onboardings, o => o.PersonalStatus),
            BuildFunnelStep("identity",   onboardings, o => o.IdentityStatus),
            BuildFunnelStep("license",    onboardings, o => o.LicenseStatus),
            BuildFunnelStep("vehicle",    onboardings, o => o.VehicleStatus),
            BuildFunnelStep("insurance",  onboardings, o => o.InsuranceStatus),
            BuildFunnelStep("guarantor",  onboardings, o => o.GuarantorStatus),
            BuildFunnelStep("payout",     onboardings, o => o.PayoutStatus),
            BuildFunnelStep("selfie",     onboardings, o => o.SelfieStatus),
        };

        var funnel = new FunnelsDto(
            SignupToFirstTrip:   Conversion(recentRiderIds.Count, convertedRiders),
            RequestToCompleted:  Conversion(totalRequests, completedRequests),
            KycDropOff:          stepDropOff);

        return AdminApiResponse.Ok(funnel);
    }

    private static FunnelStepDto BuildFunnelStep(string name,
        IEnumerable<Domain.Entities.DriverOnboarding> onboardings,
        Func<Domain.Entities.DriverOnboarding, OnboardingStepStatus> stepSelector)
    {
        var all      = onboardings.Count();
        var submitted = onboardings.Count(o => stepSelector(o) != OnboardingStepStatus.Empty);
        var approved  = onboardings.Count(o => stepSelector(o) == OnboardingStepStatus.Approved);
        var dropPct   = submitted > 0 ? Math.Round((submitted - approved) / (decimal)submitted * 100, 1) : 0;
        return new FunnelStepDto(name, submitted, approved, dropPct);
    }

    private static ConversionDto Conversion(int total, int converted)
    {
        var pct = total > 0 ? Math.Round((decimal)converted / total * 100, 1) : 0;
        return new ConversionDto(total, converted, pct);
    }
}

// ── GET /admin/metrics/cohorts ────────────────────────────────────────────────

public record GetCohortsQuery(string Market) : IRequest<object>;

public class GetCohortsHandler(IApplicationDbContext db) : IRequestHandler<GetCohortsQuery, object>
{
    public async Task<object> Handle(GetCohortsQuery req, CancellationToken ct)
    {
        // Last 8 signup cohort weeks
        var eightWeeksAgo = DateTime.UtcNow.AddDays(-56);

        var riders = await db.Users
            .Where(u => u.Role == UserRole.Rider && u.CreatedAt >= eightWeeksAgo)
            .Select(u => new { u.Id, u.CreatedAt })
            .ToListAsync(ct);

        var tripsByRider = await db.Trips
            .Where(t => t.Market == req.Market
                     && t.JobState == JobState.Completed
                     && t.CompletedAt >= eightWeeksAgo)
            .Select(t => new { t.RiderId, t.CompletedAt })
            .ToListAsync(ct);

        var cohorts = riders
            .GroupBy(r => GetIsoWeekStart(r.CreatedAt))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var weekStart = g.Key;
                var riderIds  = g.Select(r => r.Id).ToHashSet();
                var count     = riderIds.Count;
                var label     = $"W{GetIsoWeek(weekStart)} {weekStart.Year}";

                var weeksSince = Enumerable.Range(0, 8).Select(w =>
                {
                    var weekS = weekStart.AddDays(w * 7);
                    var weekE = weekS.AddDays(7);
                    var trips = tripsByRider
                        .Where(t => riderIds.Contains(t.RiderId)
                                 && t.CompletedAt >= weekS
                                 && t.CompletedAt < weekE)
                        .Count();
                    return new CohortWeekDto(w, count > 0 ? Math.Round((decimal)trips / count, 2) : 0);
                });

                return new CohortDto(label, count, weeksSince);
            })
            .ToArray();

        return AdminApiResponse.Ok(new { cohorts });
    }

    private static DateTime GetIsoWeekStart(DateTime date)
    {
        var dow = (int)date.DayOfWeek;
        var diff = dow == 0 ? 6 : dow - 1; // Monday = 0
        return date.Date.AddDays(-diff);
    }

    private static int GetIsoWeek(DateTime date)
        => System.Globalization.CultureInfo.InvariantCulture.Calendar
            .GetWeekOfYear(date, System.Globalization.CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
}
