using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Profile.Dtos;
using Izigo.Application.Features.Profile.Queries;
using Izigo.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Profile.Commands;

public record UpdatePreferencesCommand(
    string UserId,
    UpdatePreferencesRequest Updates
) : IRequest<PreferencesDto>;

public class UpdatePreferencesHandler(IApplicationDbContext db)
    : IRequestHandler<UpdatePreferencesCommand, PreferencesDto>
{
    private static readonly string[] AllowedThemes = ["light", "dark", "system"];
    private static readonly string[] AllowedLanguages = ["fr", "en"];

    public async Task<PreferencesDto> Handle(UpdatePreferencesCommand req, CancellationToken ct)
    {
        var u = req.Updates;

        if (u.Language != null && !AllowedLanguages.Contains(u.Language.ToLower()))
            throw new ArgumentException("VALIDATION_ERROR: Unsupported language.");

        if (u.Theme != null && !AllowedThemes.Contains(u.Theme.ToLower()))
            throw new ArgumentException("VALIDATION_ERROR: Theme must be light, dark or system.");

        var existing = await db.UserPreferences
            .Where(p => p.UserId == req.UserId)
            .ToDictionaryAsync(p => p.Key, ct);

        void Set(string key, string? value)
        {
            if (value == null) return;
            if (existing.TryGetValue(key, out var pref))
                pref.Value = value;
            else
                db.UserPreferences.Add(new UserPreference { UserId = req.UserId, Key = key, Value = value });
        }

        Set("incoming_trip_requests", u.IncomingTripRequests?.ToString().ToLower());
        Set("earnings_payout_alerts", u.EarningsPayoutAlerts?.ToString().ToLower());
        Set("promotional_peak_hours", u.PromotionalPeakHours?.ToString().ToLower());
        Set("trip_updates", u.TripUpdates?.ToString().ToLower());
        Set("chat_messages", u.ChatMessages?.ToString().ToLower());
        Set("language", u.Language?.ToLower());
        Set("theme", u.Theme?.ToLower());
        Set("marketing_opt_in", u.MarketingOptIn?.ToString().ToLower());

        await db.SaveChangesAsync(ct);

        return await new GetPreferencesHandler(db).Handle(new GetPreferencesQuery(req.UserId), ct);
    }
}
