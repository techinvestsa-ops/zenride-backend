using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Finance.Queries;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record FareRuleDto(string Id, string ServiceClass, long Base, long PerKm, long PerMin,
    long Minimum, long WaitingPerMin, long CancellationFee, bool IsActive,
    int Version, DateTime EffectiveFrom, string? UpdatedByStaffId, string Market);

public record CommissionConfigDto(decimal CommissionRate, decimal BonusRate, string BonusLabel,
    long CashSettlementCap, string VerticalOverridesJson, int Version,
    DateTime EffectiveFrom, string Market);

public record SurgeZoneDto(string ZoneId, string ZoneName, decimal Multiplier,
    bool IsAutomatic, bool IsActive, DateTime? ExpiresAt);

public record SimulateResultDto(int TripsAnalysed, long CurrentRevenue, long ProjectedRevenue,
    long Delta, decimal DeltaPct, string Currency);

// ── GET /admin/pricing/fare-rules ────────────────────────────────────────────

public record GetFareRulesQuery(string Market) : IRequest<object>;

public class GetFareRulesHandler(IApplicationDbContext db)
    : IRequestHandler<GetFareRulesQuery, object>
{
    public async Task<object> Handle(GetFareRulesQuery req, CancellationToken ct)
    {
        var rules = await db.FareRules
            .Where(r => r.Market == req.Market && r.IsActive)
            .Select(r => new FareRuleDto(r.Id, r.ServiceClass.ToString(),
                r.Base, r.PerKm, r.PerMin, r.Minimum, r.WaitingPerMin, r.CancellationFee,
                r.IsActive, r.Version, r.EffectiveFrom, r.UpdatedByStaffId, r.Market))
            .ToListAsync(ct);

        return AdminApiResponse.Ok(rules);
    }
}

// ── GET /admin/pricing/fare-rules/history ─────────────────────────────────────

public record GetFareRuleHistoryQuery(string Market, string? ServiceClass) : IRequest<object>;

public class GetFareRuleHistoryHandler(IApplicationDbContext db)
    : IRequestHandler<GetFareRuleHistoryQuery, object>
{
    public async Task<object> Handle(GetFareRuleHistoryQuery req, CancellationToken ct)
    {
        var query = db.FareRules.Where(r => r.Market == req.Market).AsQueryable();

        if (!string.IsNullOrWhiteSpace(req.ServiceClass) &&
            Enum.TryParse<ServiceClass>(req.ServiceClass, true, out var sc))
            query = query.Where(r => r.ServiceClass == sc);

        var history = await query
            .OrderByDescending(r => r.Version)
            .Select(r => new FareRuleDto(r.Id, r.ServiceClass.ToString(),
                r.Base, r.PerKm, r.PerMin, r.Minimum, r.WaitingPerMin, r.CancellationFee,
                r.IsActive, r.Version, r.EffectiveFrom, r.UpdatedByStaffId, r.Market))
            .ToListAsync(ct);

        return AdminApiResponse.Ok(history);
    }
}

// ── GET /admin/pricing/commission ────────────────────────────────────────────
// Resolves the 20%/25% commission disagreement in the driver app.

public record GetCommissionQuery(string Market) : IRequest<CommissionConfigDto?>;

public class GetCommissionHandler(IApplicationDbContext db)
    : IRequestHandler<GetCommissionQuery, CommissionConfigDto?>
{
    public async Task<CommissionConfigDto?> Handle(GetCommissionQuery req, CancellationToken ct)
    {
        var config = await db.CommissionConfigs
            .Where(c => c.Market == req.Market)
            .OrderByDescending(c => c.Version)
            .Select(c => new CommissionConfigDto(c.CommissionRate, c.BonusRate, c.BonusLabel,
                c.CashSettlementCap, c.VerticalOverridesJson, c.Version, c.EffectiveFrom, c.Market))
            .FirstOrDefaultAsync(ct);

        // Return sensible defaults if no config exists yet
        return config ?? new CommissionConfigDto(0.20m, 0.035m, "Bonus", 10_000, "{}", 1,
            DateTime.UtcNow, req.Market);
    }
}

// ── GET /admin/pricing/surge ──────────────────────────────────────────────────

public record GetSurgeQuery(string Market) : IRequest<object>;

public class GetSurgeHandler(IApplicationDbContext db)
    : IRequestHandler<GetSurgeQuery, object>
{
    public async Task<object> Handle(GetSurgeQuery req, CancellationToken ct)
    {
        var zones = await db.Zones
            .Where(z => z.Market == req.Market)
            .Select(z => new { z.Id, z.Name })
            .ToListAsync(ct);

        var zoneIds = zones.Select(z => z.Id).ToList();
        var now     = DateTime.UtcNow;

        var surges = await db.SurgeZones
            .Where(s => zoneIds.Contains(s.ZoneId)
                     && (s.ExpiresAt == null || s.ExpiresAt > now))
            .Select(s => new { s.ZoneId, s.Multiplier, s.IsAutomatic, s.IsActive, s.ExpiresAt })
            .ToDictionaryAsync(s => s.ZoneId, ct);

        var result = zones.Select(z =>
        {
            surges.TryGetValue(z.Id, out var surge);
            return new SurgeZoneDto(z.Id, z.Name,
                surge?.Multiplier ?? 1.0m,
                surge?.IsAutomatic ?? true,
                surge?.IsActive ?? false,
                surge?.ExpiresAt);
        }).ToArray();

        return AdminApiResponse.Ok(result);
    }
}

// ── POST /admin/pricing/simulate ─────────────────────────────────────────────
// Replays last week's trips against proposed rules, returns revenue delta.

public record SimulateFareQuery(string Market, string ServiceClass,
    long Base, long PerKm, long PerMin, long Minimum) : IRequest<SimulateResultDto>;

public class SimulateFareHandler(IApplicationDbContext db)
    : IRequestHandler<SimulateFareQuery, SimulateResultDto>
{
    public async Task<SimulateResultDto> Handle(SimulateFareQuery req, CancellationToken ct)
    {
        if (!Enum.TryParse<ServiceClass>(req.ServiceClass, true, out var sc))
            sc = ServiceClass.ZenCar;

        var lastWeek = DateTime.UtcNow.AddDays(-7);
        var trips = await db.Trips
            .Where(t => t.Market == req.Market
                     && t.ServiceClass == sc
                     && t.JobState == JobState.Completed
                     && t.CompletedAt >= lastWeek)
            .Select(t => new { t.FareGross, t.DistanceM, t.DurationS })
            .ToListAsync(ct);

        if (!trips.Any())
            return new SimulateResultDto(0, 0, 0, 0, 0, "XOF");

        var currentRevenue = trips.Sum(t => (long)t.FareGross);

        // Apply proposed rules to each trip
        var projectedRevenue = trips.Sum(t =>
        {
            var distanceKm = (t.DistanceM ?? 0) / 1000.0m;
            var durationMin = (t.DurationS ?? 0) / 60.0m;
            var fare = req.Base
                     + (long)(distanceKm * req.PerKm)
                     + (long)(durationMin * req.PerMin);
            return Math.Max(fare, req.Minimum);
        });

        var delta    = projectedRevenue - currentRevenue;
        var deltaPct = currentRevenue > 0
            ? Math.Round((decimal)delta / currentRevenue * 100, 1) : 0;

        return new SimulateResultDto(trips.Count, currentRevenue, projectedRevenue, delta, deltaPct, "XOF");
    }
}
