using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Kyc;

// Onboarding rows are keyed by DriverProfile.Id, but documents, vehicles and SignalR groups use the user id.
internal static class KycDriverLookup
{
    public static async Task<DriverProfile?> FindDriverAsync(
        IApplicationDbContext db, string driverId, CancellationToken ct) =>
        await db.DriverProfiles
            .FirstOrDefaultAsync(d => d.Id == driverId || d.UserId == driverId, ct);

    public static string[] OwnerIds(DriverProfile? driver, string driverId) =>
        driver is null ? [driverId] : [driver.Id, driver.UserId];

    public static async Task<DriverOnboarding?> FindOnboardingAsync(
        IApplicationDbContext db, string[] ownerIds, CancellationToken ct) =>
        await db.DriverOnboardings
            .Where(o => ownerIds.Contains(o.DriverId))
            .OrderByDescending(o => o.SubmittedAt)
            .FirstOrDefaultAsync(ct);
}
