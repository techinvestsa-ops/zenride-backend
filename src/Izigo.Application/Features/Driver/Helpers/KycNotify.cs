using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Driver.Helpers;

/// <summary>Admin queue + driver push for KYC lifecycle.</summary>
internal static class KycNotify
{
    public static async Task<string> ResolveAdminMarketAsync(
        IApplicationDbContext db, string driverUserId, CancellationToken ct)
    {
        var currency = await db.Wallets
            .Where(w => w.UserId == driverUserId)
            .Select(w => w.Currency)
            .FirstOrDefaultAsync(ct);
        return currency?.ToUpperInvariant() switch
        {
            "NGN" => "ng",
            _     => "ci",
        };
    }

    public static async Task NotifyAdminNewApplicationAsync(
        IApplicationDbContext db,
        IRealtimeService realtime,
        string driverUserId,
        CancellationToken ct)
    {
        var row = await db.DriverProfiles
            .Where(d => d.UserId == driverUserId)
            .Select(d => new { Name = (d.User.FirstName + " " + d.User.LastName).Trim(), ProfileId = d.Id })
            .FirstOrDefaultAsync(ct);
        if (row is null) return;

        var market = await ResolveAdminMarketAsync(db, driverUserId, ct);
        var name = string.IsNullOrWhiteSpace(row.Name) ? "Driver" : row.Name;

        await PublishAdminKycEventAsync(realtime, market, row.ProfileId, name, step: null, resubmit: false, ct);
    }

    /// <summary>When a step is saved while the application is already in the admin queue.</summary>
    public static async Task AfterDriverStepSavedAsync(
        IApplicationDbContext db,
        IRealtimeService realtime,
        string driverUserId,
        Domain.Entities.DriverOnboarding onb,
        string stepKey,
        CancellationToken ct)
    {
        if (onb.SubmittedAt is null) return;

        var row = await db.DriverProfiles
            .Where(d => d.UserId == driverUserId)
            .Select(d => new { Name = (d.User.FirstName + " " + d.User.LastName).Trim(), ProfileId = d.Id })
            .FirstOrDefaultAsync(ct);
        if (row is null) return;

        var market = await ResolveAdminMarketAsync(db, driverUserId, ct);
        var name = string.IsNullOrWhiteSpace(row.Name) ? "Driver" : row.Name;
        await PublishAdminKycEventAsync(realtime, market, row.ProfileId, name, stepKey, resubmit: true, ct);
    }

    public static async Task NotifyDriverKycStatusAsync(
        IApplicationDbContext db,
        IRealtimeService realtime,
        IPushService push,
        string driverUserId,
        string status,
        string? step,
        string? reason,
        IReadOnlyList<string>? rejectedSteps,
        string pushTitle,
        string pushBody,
        CancellationToken ct)
    {
        var steps = rejectedSteps ?? (step is not null ? new[] { step } : Array.Empty<string>());

        await realtime.PublishToDriverAsync(driverUserId, "kyc.status_changed", new
        {
            status = status.ToLowerInvariant(),
            step,
            reason,
            rejected_steps = steps,
        }, ct);

        var token = await LatestFcmTokenAsync(db, driverUserId, ct);
        if (token is null) return;

        await push.SendAsync(
            token,
            pushTitle,
            pushBody,
            type: "kyc.status_changed",
            entityId: driverUserId,
            deepLink: "izigo://onboarding/kyc",
            highPriority: status.Equals("rejected", StringComparison.OrdinalIgnoreCase),
            ct: ct);
    }

    private static async Task PublishAdminKycEventAsync(
        IRealtimeService realtime,
        string market,
        string driverProfileId,
        string driverName,
        string? step,
        bool resubmit,
        CancellationToken ct)
    {
        await realtime.PublishToAdminAsync(market, "kyc.submitted", new
        {
            driver_id   = driverProfileId,
            driver_name = driverName,
            step,
            resubmit,
        }, ct);

        await realtime.PublishToAdminAsync(market, "console.invalidate", new { scope = "kyc" }, ct);
    }

    private static Task<string?> LatestFcmTokenAsync(
        IApplicationDbContext db, string userId, CancellationToken ct)
        => db.UserDevices
            .Where(d => d.UserId == userId && d.FcmToken != null)
            .OrderByDescending(d => d.LastActiveAt)
            .Select(d => d.FcmToken!)
            .FirstOrDefaultAsync(ct);
}
