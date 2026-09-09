using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Quotes.Dtos;
using Izigo.Application.Features.Quotes.Helpers;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Quotes.Queries;

// ── GET /quotes/{quote_id} — 410 once expired ─────────────────────────────────

public record GetQuoteQuery(string UserId, string QuoteId) : IRequest<QuoteDto?>;

public class GetQuoteHandler(IApplicationDbContext db)
    : IRequestHandler<GetQuoteQuery, QuoteDto?>
{
    public async Task<QuoteDto?> Handle(GetQuoteQuery req, CancellationToken ct)
    {
        var q = await db.Quotes
            .FirstOrDefaultAsync(x => x.Id == req.QuoteId && x.RiderId == req.UserId, ct);

        // Return null for missing or expired quotes — controller maps null → 410
        if (q == null || q.ExpiresAt <= DateTime.UtcNow)
            return null;

        var options = System.Text.Json.JsonSerializer
            .Deserialize<QuoteOptionDto[]>(q.OptionsJson) ?? [];

        return new QuoteDto(
            QuoteId: q.Id,
            ExpiresAt: q.ExpiresAt,
            Currency: q.Currency,
            Pickup: new QuoteLocationDto(q.PickupLabel, (double)q.PickupLat, (double)q.PickupLng, null),
            Dropoff: new QuoteLocationDto(q.DropoffLabel, (double)q.DropoffLat, (double)q.DropoffLng, null),
            DistanceM: q.DistanceM,
            DurationS: q.DurationS,
            EncodedPolyline: q.EncodedPolyline,
            Surge: new SurgeDto(q.SurgeActive, (double)q.SurgeMultiplier, q.SurgeReason),
            Options: options,
            PaymentMethods: ["cash", "wallet", "orange_money", "moov_money", "wave"]);
    }
}

// ── GET /fare-rules ────────────────────────────────────────────────────────────

public record GetFareRulesQuery(string Market) : IRequest<List<FareRuleDto>>;

public class GetFareRulesHandler(IApplicationDbContext db)
    : IRequestHandler<GetFareRulesQuery, List<FareRuleDto>>
{
    public async Task<List<FareRuleDto>> Handle(GetFareRulesQuery req, CancellationToken ct)
    {
        var dbRules = await db.FareRules
            .Where(r => r.IsActive && (r.Market == req.Market || r.Market == ""))
            .ToListAsync(ct);

        // Return rules for all service classes; fill gaps with defaults
        var classes = Enum.GetValues<ServiceClass>();
        return classes.Select(sc =>
        {
            var rule = dbRules.FirstOrDefault(r => r.ServiceClass == sc)
                       ?? FareCalculator.DefaultRule(sc);
            return new FareRuleDto(
                ClassCode: sc.ToString().ToLower(),
                Base: rule.Base,
                PerKm: rule.PerKm,
                PerMin: rule.PerMin,
                Minimum: rule.Minimum,
                WaitingPerMin: rule.WaitingPerMin,
                CancellationFee: rule.CancellationFee);
        }).ToList();
    }
}
