using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Profile.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Profile.Queries;

public record GetPreferencesQuery(string UserId) : IRequest<PreferencesDto>;

public class GetPreferencesHandler(IApplicationDbContext db) : IRequestHandler<GetPreferencesQuery, PreferencesDto>
{
    public async Task<PreferencesDto> Handle(GetPreferencesQuery req, CancellationToken ct)
    {
        var prefs = await db.UserPreferences
            .Where(p => p.UserId == req.UserId)
            .ToDictionaryAsync(p => p.Key, p => p.Value, ct);

        return new PreferencesDto(
            IncomingTripRequests: GetBool(prefs, "incoming_trip_requests", true),
            EarningsPayoutAlerts: GetBool(prefs, "earnings_payout_alerts", true),
            PromotionalPeakHours: GetBool(prefs, "promotional_peak_hours", true),
            TripUpdates: GetBool(prefs, "trip_updates", true),
            ChatMessages: GetBool(prefs, "chat_messages", true),
            Language: prefs.GetValueOrDefault("language", "fr"),
            Theme: prefs.GetValueOrDefault("theme", "system"),
            MarketingOptIn: GetBool(prefs, "marketing_opt_in", false)
        );
    }

    private static bool GetBool(Dictionary<string, string> d, string key, bool def)
        => d.TryGetValue(key, out var v) ? v == "true" : def;
}
