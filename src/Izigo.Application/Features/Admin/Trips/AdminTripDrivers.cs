using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Trips;

/// <summary>
/// Trip.DriverId is the driver's user id. Offer expiry clears it, so expired
/// trips that did get a driver still need the last Offered history actor.
/// </summary>
internal static class AdminTripDrivers
{
    internal static async Task<Dictionary<string, string>> LoadNamesAsync(
        IApplicationDbContext db, IEnumerable<string?> userIds, CancellationToken ct)
    {
        var ids = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id!).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, string>();

        var users = await db.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName })
            .ToListAsync(ct);

        return users.ToDictionary(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());
    }

    internal static async Task<Dictionary<string, string>> LastOfferedDriverAsync(
        IApplicationDbContext db, IEnumerable<string> tripIds, CancellationToken ct)
    {
        var ids = tripIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<string, string>();

        var history = await db.TripStateHistories
            .Where(h => ids.Contains(h.TripId) &&
                        h.State == JobState.Offered &&
                        h.ActorId != null)
            .Select(h => new { h.TripId, h.ActorId, h.OccurredAt })
            .ToListAsync(ct);

        return history
            .GroupBy(h => h.TripId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.OccurredAt).First().ActorId!);
    }

    internal static string DisplayState(JobState state, bool wasOffered) => state switch
    {
        JobState.Broadcasting => "searching",
        JobState.Offered => "offered",
        JobState.Expired when wasOffered => "offer_expired",
        JobState.Expired => "no_drivers_found",
        _ => Ops.Queries.GetLiveOpsHandler.ToDisplayState(state)
    };
}
