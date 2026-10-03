using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Quotes.Helpers;

/// <summary>
/// Pickup wait is free until the grace period ends. Each whole minute after
/// that adds the fare rule's per-minute waiting charge to the rider's fare.
/// </summary>
public static class WaitingFare
{
    public static (int ElapsedMin, int BillableMin, long Fee) Accrue(
        DateTime arrivedAt, DateTime asOf, int graceMin, long perMin)
    {
        if (graceMin < 0) graceMin = 0;
        if (perMin < 0) perMin = 0;
        var elapsed = (int)Math.Floor((asOf - arrivedAt).TotalMinutes);
        if (elapsed < 0) elapsed = 0;
        var billable = Math.Max(0, elapsed - graceMin);
        return (elapsed, billable, billable * perMin);
    }

    public static async Task<long> ApplyAsync(IApplicationDbContext db, Trip trip, DateTime asOf, CancellationToken ct)
    {
        if (trip.ArrivedAt is null)
            return 0;

        var rule = await db.FareRules
            .Where(r => r.IsActive && r.ServiceClass == trip.ServiceClass && r.Market == trip.Market)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(ct);

        var grace = rule?.WaitGraceMin ?? 10;
        var perMin = rule?.WaitingPerMin ?? 0;
        var (_, _, fee) = Accrue(trip.ArrivedAt.Value, asOf, grace, perMin);
        trip.FareWaiting = fee;
        return fee;
    }
}
