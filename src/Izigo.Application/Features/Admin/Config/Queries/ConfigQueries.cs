using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Application.Common.Settings;
using Izigo.Application.Features.Admin.Config.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Izigo.Application.Features.Admin.Config.Queries;

// ── GET /admin/config ─────────────────────────────────────────────────────────────

public record GetAdminConfigQuery(string Market) : IRequest<object>;

public class GetAdminConfigHandler(IApplicationDbContext db, IOptions<DispatchSettings> dispatchOptions)
    : IRequestHandler<GetAdminConfigQuery, object>
{
    public async Task<object> Handle(GetAdminConfigQuery req, CancellationToken ct)
    {
        var dispatch = await db.DispatchConfigs
            .FirstOrDefaultAsync(d => d.Market == req.Market, ct);

        var flags = await db.FeatureFlags
            .Where(f => f.Market == req.Market || f.Scope == "global")
            .Select(f => new FeatureFlagDto(f.Key, f.Description, f.Enabled, f.RolloutPct, f.Scope))
            .ToListAsync(ct);

        var dispatchDto = dispatch is not null
            ? new DispatchConfigDto(
                dispatch.Market, dispatch.OfferTimeoutSeconds, dispatch.SearchRadiusM,
                dispatch.Strategy, dispatch.MaxConcurrentOffers,
                dispatch.LocationPingOnTripS, dispatch.LocationPingIdleS,
                dispatch.UpdatedByStaffId, dispatch.UpdatedAt)
            : new DispatchConfigDto(req.Market,
                dispatchOptions.Value.OfferTimeoutSeconds, dispatchOptions.Value.SearchRadiusM,
                dispatchOptions.Value.Strategy, dispatchOptions.Value.MaxConcurrentOffers,
                dispatchOptions.Value.LocationPingOnTripS, dispatchOptions.Value.LocationPingIdleS,
                null, null);

        return AdminApiResponse.Ok(new { dispatch = dispatchDto, flags });
    }
}

// ── GET /admin/config/flags ───────────────────────────────────────────────────────

public record GetFeatureFlagsQuery(string Market) : IRequest<object>;

public class GetFeatureFlagsHandler(IApplicationDbContext db)
    : IRequestHandler<GetFeatureFlagsQuery, object>
{
    // The 10 canonical flag keys with descriptions
    private static readonly (string Key, string Desc)[] CanonicalFlags =
    [
        ("co_ride_enabled",   "Enable co-ride (shared) vertical"),
        ("package_enabled",   "Enable package delivery vertical"),
        ("food_enabled",      "Enable food ordering vertical"),
        ("scheduled_rides",   "Allow riders to schedule future rides"),
        ("tipping",           "Allow riders to tip drivers"),
        ("wallet_transfer",   "Allow wallet-to-wallet transfers"),
        ("instant_payout",    "Allow drivers to request instant payouts"),
        ("referrals",         "Enable referral programme"),
        ("masked_calls",      "Mask phone numbers during calls"),
        ("biometric_login",   "Allow biometric authentication login")
    ];

    public async Task<object> Handle(GetFeatureFlagsQuery req, CancellationToken ct)
    {
        var stored = await db.FeatureFlags
            .Where(f => f.Market == req.Market || f.Scope == "global")
            .ToListAsync(ct);

        var result = CanonicalFlags.Select(canonical =>
        {
            var f = stored.FirstOrDefault(x => x.Key == canonical.Key);
            return new FeatureFlagDto(
                canonical.Key, canonical.Desc,
                f?.Enabled ?? false,
                f?.RolloutPct ?? 100,
                f?.Scope ?? "global");
        }).ToList();

        return AdminApiResponse.Ok(result, new AdminPagedMeta(1, 100, result.Count, 1));
    }
}

// ── GET /admin/integrations ───────────────────────────────────────────────────────

public record GetIntegrationsQuery(string Market) : IRequest<object>;

public class GetIntegrationsHandler(IApplicationDbContext db)
    : IRequestHandler<GetIntegrationsQuery, object>
{
    public async Task<object> Handle(GetIntegrationsQuery req, CancellationToken ct)
    {
        var integrations = await db.Integrations
            .Where(i => i.Market == req.Market)
            .Select(i => new IntegrationDto(i.Key, i.Label, i.IsConnected, i.LastError, i.KeyLastRotatedAt))
            .ToListAsync(ct);

        return AdminApiResponse.Ok(integrations, new AdminPagedMeta(1, 100, integrations.Count, 1));
    }
}
