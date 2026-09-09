using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Ops.Commands;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record OpsCommandResult(bool Success, string? ErrorCode);

// ── POST /admin/ops/jobs/{id}/reassign ───────────────────────────────────────
// driver_id? (omit to re-broadcast), reason — must notify both drivers and the rider

public record ReassignJobCommand(string JobId, string? DriverId, string Reason,
    string StaffId, string StaffName, string Market) : IRequest<OpsCommandResult>;

public class ReassignJobHandler(IApplicationDbContext db, IAuditService audit,
    IPushService push, IRealtimeService realtime)
    : IRequestHandler<ReassignJobCommand, OpsCommandResult>
{
    private static readonly JobState[] ActiveStates =
    [
        JobState.Broadcasting, JobState.Offered, JobState.Accepted,
        JobState.EnRouteToPickup, JobState.ArrivedAtPickup
    ];

    public async Task<OpsCommandResult> Handle(ReassignJobCommand cmd, CancellationToken ct)
    {
        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == cmd.JobId, ct);
        if (trip is null) return new(false, "TRIP_NOT_FOUND");
        if (!ActiveStates.Contains(trip.JobState)) return new(false, "TRIP_NOT_ACTIVE");

        var prevDriverId = trip.DriverId;
        var before = new { trip.DriverId, trip.JobState };

        if (cmd.DriverId is null)
        {
            // Re-broadcast — clear current driver assignment
            trip.DriverId    = null;
            trip.JobState    = JobState.Broadcasting;
            trip.AssignedAt  = null;
        }
        else
        {
            var newDriver = await db.DriverProfiles.AnyAsync(d => d.Id == cmd.DriverId, ct);
            if (!newDriver) return new(false, "DRIVER_NOT_FOUND");

            trip.DriverId   = cmd.DriverId;
            trip.JobState   = JobState.Accepted;
            trip.AssignedAt = DateTime.UtcNow;
        }

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.TripAdjust,
            "Trip", trip.Id, cmd.Reason, before,
            new { trip.DriverId, trip.JobState }, market: cmd.Market, ct: ct);

        await db.SaveChangesAsync(ct);

        // Notify previous driver (if any)
        if (prevDriverId is not null)
        {
            await realtime.PublishToDriverAsync(prevDriverId, "job.updated",
                new { job_id = trip.Id, state = "reassigned" }, ct);
        }

        // Notify new driver (if assigned)
        if (cmd.DriverId is not null)
        {
            await realtime.PublishToDriverAsync(cmd.DriverId, "job.offered",
                new { job_id = trip.Id }, ct);
        }

        // Notify rider
        await realtime.PublishToUserAsync(trip.RiderId, "ride.status_changed",
            new { ride_id = trip.Id, status = "searching" }, ct);

        return new(true, null);
    }
}

// ── POST /admin/ops/jobs/{id}/cancel ─────────────────────────────────────────
// reason, waive_fee, compensate_driver — sets cancelled_by_admin

public record CancelJobCommand(string JobId, string Reason, bool WaiveFee, bool CompensateDriver,
    string StaffId, string StaffName, string Market) : IRequest<OpsCommandResult>;

public class CancelJobHandler(IApplicationDbContext db, IAuditService audit, IRealtimeService realtime)
    : IRequestHandler<CancelJobCommand, OpsCommandResult>
{
    private static readonly JobState[] CancellableStates =
    [
        JobState.Broadcasting, JobState.Offered, JobState.Accepted,
        JobState.EnRouteToPickup, JobState.ArrivedAtPickup,
        JobState.PickedUp, JobState.EnRouteToDropoff, JobState.ArrivedAtDropoff
    ];

    public async Task<OpsCommandResult> Handle(CancelJobCommand cmd, CancellationToken ct)
    {
        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == cmd.JobId, ct);
        if (trip is null) return new(false, "TRIP_NOT_FOUND");
        if (!CancellableStates.Contains(trip.JobState)) return new(false, "TRIP_NOT_CANCELLABLE");

        var before = new { trip.JobState, trip.CancellationFee, trip.DriverEarnings };

        trip.JobState    = JobState.CancelledByAdmin;
        trip.CancelledAt = DateTime.UtcNow;
        trip.CancellationReasonCode = cmd.Reason;

        if (cmd.WaiveFee) trip.CancellationFee = 0;

        if (cmd.CompensateDriver && trip.DriverId is not null)
        {
            var driverWallet = await db.DriverWallets
                .FirstOrDefaultAsync(w => w.DriverId == trip.DriverId, ct);
            if (driverWallet is not null)
            {
                var compensation = trip.FareGross / 4; // 25% as compensation
                driverWallet.AvailableBalance += compensation;
                db.DriverWalletTransactions.Add(new Domain.Entities.DriverWalletTransaction
                {
                    DriverWalletId = driverWallet.Id,
                    Type           = "admin_compensation",
                    Amount         = compensation,
                    BalanceAfter   = driverWallet.AvailableBalance,
                    ReferenceId    = trip.Id,
                    Reason         = $"Admin cancel compensation: {cmd.Reason}",
                    CreatedBy      = cmd.StaffId
                });
            }
        }

        // Add state history
        db.TripStateHistories.Add(new Domain.Entities.TripStateHistory
        {
            TripId      = trip.Id,
            State       = JobState.CancelledByAdmin,
            OccurredAt  = DateTime.UtcNow,
            Actor       = "admin",
            ActorId     = cmd.StaffId
        });

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.TripAdjust,
            "Trip", trip.Id, cmd.Reason, before,
            new { trip.JobState, trip.CancellationFee }, market: cmd.Market, ct: ct);

        await db.SaveChangesAsync(ct);

        // Notify rider and driver
        await realtime.PublishToUserAsync(trip.RiderId, "ride.status_changed",
            new { ride_id = trip.Id, status = "cancelled" }, ct);

        if (trip.DriverId is not null)
        {
            await realtime.PublishToDriverAsync(trip.DriverId, "job.updated",
                new { job_id = trip.Id, state = "cancelled_by_admin" }, ct);
        }

        return new(true, null);
    }
}

// ── POST /admin/ops/drivers/{id}/force-offline ───────────────────────────────
// reason — pushes driver.forced_offline so the app's toggle updates immediately

public record ForceDriverOfflineCommand(string DriverId, string Reason,
    string StaffId, string StaffName, string Market) : IRequest<OpsCommandResult>;

public class ForceDriverOfflineHandler(IApplicationDbContext db, IAuditService audit,
    IRealtimeService realtime)
    : IRequestHandler<ForceDriverOfflineCommand, OpsCommandResult>
{
    public async Task<OpsCommandResult> Handle(ForceDriverOfflineCommand cmd, CancellationToken ct)
    {
        var driver = await db.DriverProfiles.FirstOrDefaultAsync(d => d.Id == cmd.DriverId, ct);
        if (driver is null) return new(false, "DRIVER_NOT_FOUND");

        var before = new { driver.IsOnline };
        driver.IsOnline = false;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.DriverSuspend,
            "Driver", cmd.DriverId, cmd.Reason, before,
            new { IsOnline = false }, market: cmd.Market, ct: ct);

        await db.SaveChangesAsync(ct);

        // Push driver.forced_offline so the app toggle updates immediately
        await realtime.PublishToDriverAsync(cmd.DriverId, "driver.forced_offline",
            new { reason = cmd.Reason }, ct);

        return new(true, null);
    }
}

// ── POST /admin/ops/broadcast-zone ───────────────────────────────────────────
// zone_id, message, optional incentive

public record BroadcastZoneCommand(string ZoneId, string Message, string? Incentive,
    string StaffId, string StaffName, string Market) : IRequest<OpsCommandResult>;

public class BroadcastZoneHandler(IApplicationDbContext db, IAuditService audit,
    IPushService push, IRealtimeService realtime)
    : IRequestHandler<BroadcastZoneCommand, OpsCommandResult>
{
    public async Task<OpsCommandResult> Handle(BroadcastZoneCommand cmd, CancellationToken ct)
    {
        var zone = await db.Zones.FirstOrDefaultAsync(z => z.Id == cmd.ZoneId, ct);
        if (zone is null) return new(false, "ZONE_NOT_FOUND");

        // Get online drivers near this zone (within 10 km of zone center)
        var allOnline = await db.DriverProfiles
            .Where(d => d.IsOnline && d.LastLat != null && d.LastLng != null)
            .Select(d => new { d.Id, d.UserId, d.LastLat, d.LastLng })
            .ToListAsync(ct);

        var nearbyDriverIds = allOnline
            .Where(d => Ops.Queries.GetLiveOpsHandler.HaversineKm(
                (double)d.LastLat!, (double)d.LastLng!,
                (double)zone.CenterLat, (double)zone.CenterLng) < 10)
            .Select(d => d.UserId)
            .ToList();

        // Get FCM tokens for those users
        var tokens = await db.UserDevices
            .Where(dev => nearbyDriverIds.Contains(dev.UserId) && dev.FcmToken != null)
            .Select(dev => dev.FcmToken!)
            .Distinct()
            .ToListAsync(ct);

        if (tokens.Any())
        {
            await push.SendBatchAsync(tokens, "Zone Request", cmd.Message,
                "zone_broadcast", deepLink: null, ct: ct);
        }

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.BroadcastSend,
            "Zone", cmd.ZoneId, reason: cmd.Message,
            after: new { zone_id = cmd.ZoneId, recipients = tokens.Count, incentive = cmd.Incentive },
            market: cmd.Market, ct: ct);

        return new(true, null);
    }
}
