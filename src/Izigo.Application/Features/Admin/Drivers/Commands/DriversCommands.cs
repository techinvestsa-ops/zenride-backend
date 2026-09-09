using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Drivers.Commands;

public record DriverCommandResult(bool Success, string? ErrorCode, object? Data = null);

// helpers
file static class DriverHelpers
{
    internal static readonly JobState[] ActiveStates =
    [
        JobState.Broadcasting, JobState.Offered, JobState.Accepted,
        JobState.EnRouteToPickup, JobState.ArrivedAtPickup,
        JobState.PickedUp, JobState.EnRouteToDropoff, JobState.ArrivedAtDropoff
    ];

    /// <summary>Cancels any active trip assigned to this driver (driver-fault handling).</summary>
    internal static async Task CancelActiveJobAsync(IApplicationDbContext db, string driverId,
        string staffId, CancellationToken ct)
    {
        var activeTrip = await db.Trips
            .FirstOrDefaultAsync(t => t.DriverId == driverId && ActiveStates.Contains(t.JobState), ct);
        if (activeTrip is null) return;

        activeTrip.JobState    = JobState.CancelledByDriver;
        activeTrip.CancelledAt = DateTime.UtcNow;
        db.TripStateHistories.Add(new Domain.Entities.TripStateHistory
        {
            TripId     = activeTrip.Id,
            State      = JobState.CancelledByDriver,
            OccurredAt = DateTime.UtcNow,
            Actor      = "admin",
            ActorId    = staffId
        });
    }
}

// ── A08: POST /admin/drivers/{id}/suspend ────────────────────────────────────
// Forces offline and cancels any active job with driver-fault handling.

public record SuspendDriverCommand(string DriverId, string Reason, DateTime? Until,
    string StaffId, string StaffName) : IRequest<DriverCommandResult>;

public class SuspendDriverHandler(IApplicationDbContext db, IAuditService audit,
    IRealtimeService realtime) : IRequestHandler<SuspendDriverCommand, DriverCommandResult>
{
    public async Task<DriverCommandResult> Handle(SuspendDriverCommand cmd, CancellationToken ct)
    {
        var driver = await db.DriverProfiles
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == cmd.DriverId, ct);
        if (driver is null) return new(false, "DRIVER_NOT_FOUND");

        var before = new { driver.User.Status, driver.IsOnline };

        driver.User.Status            = UserStatus.Suspended;
        driver.User.SuspensionReason  = cmd.Reason;
        driver.User.SuspendedUntil    = cmd.Until;
        driver.IsOnline               = false; // force offline

        // Cancel active job (driver-fault handling)
        await DriverHelpers.CancelActiveJobAsync(db, cmd.DriverId, cmd.StaffId, ct);

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.DriverSuspend,
            "Driver", cmd.DriverId, cmd.Reason, before,
            new { Status = "Suspended", IsOnline = false, cmd.Until }, ct: ct);
        await db.SaveChangesAsync(ct);

        // Push forced-offline event so the app toggle updates
        await realtime.PublishToDriverAsync(cmd.DriverId, "driver.forced_offline",
            new { reason = cmd.Reason }, ct);

        return new(true, null);
    }
}

// ── A08: PATCH /admin/drivers/{id}/verticals ──────────────────────────────────
// Constrained by vehicle type.

public record UpdateDriverVerticalsCommand(string DriverId, List<string> Verticals,
    string StaffId, string StaffName) : IRequest<DriverCommandResult>;

public class UpdateDriverVerticalsHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UpdateDriverVerticalsCommand, DriverCommandResult>
{
    public async Task<DriverCommandResult> Handle(UpdateDriverVerticalsCommand cmd, CancellationToken ct)
    {
        var driver = await db.DriverProfiles.FirstOrDefaultAsync(d => d.Id == cmd.DriverId, ct);
        if (driver is null) return new(false, "DRIVER_NOT_FOUND");

        var newVerticals = new List<Vertical>();
        foreach (var v in cmd.Verticals)
        {
            if (!Enum.TryParse<Vertical>(v, true, out var vertical))
                return new(false, "INVALID_VERTICAL");
            newVerticals.Add(vertical);
        }

        // Validate against vehicle type (bikes cannot serve Package)
        var vehicle = await db.Vehicles
            .FirstOrDefaultAsync(v => v.DriverId == cmd.DriverId && v.IsActive, ct);
        if (vehicle is not null && vehicle.Type == VehicleType.Bike &&
            newVerticals.Contains(Vertical.Package))
            return new(false, "VEHICLE_TYPE_INCOMPATIBLE");

        var before = new { Verticals = driver.VerticalsAllowed };
        driver.VerticalsAllowed = newVerticals;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.DriverSuspend,
            "Driver", cmd.DriverId, reason: "Verticals updated",
            before: before, after: new { Verticals = newVerticals }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── A08: PATCH /admin/drivers/{id}/vehicle ────────────────────────────────────
// Plate or type change re-triggers review.

public record UpdateDriverVehicleCommand(string DriverId, string? Plate, string? VehicleType,
    string StaffId, string StaffName) : IRequest<DriverCommandResult>;

public class UpdateDriverVehicleHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UpdateDriverVehicleCommand, DriverCommandResult>
{
    public async Task<DriverCommandResult> Handle(UpdateDriverVehicleCommand cmd, CancellationToken ct)
    {
        var vehicle = await db.Vehicles
            .FirstOrDefaultAsync(v => v.DriverId == cmd.DriverId && v.IsActive, ct);
        if (vehicle is null) return new(false, "VEHICLE_NOT_FOUND");

        var before = new { vehicle.Plate, vehicle.Type };
        bool retrigger = false;

        if (cmd.Plate is not null && cmd.Plate != vehicle.Plate)
        {
            vehicle.Plate = cmd.Plate;
            retrigger     = true;
        }

        if (!string.IsNullOrWhiteSpace(cmd.VehicleType) &&
            Enum.TryParse<VehicleType>(cmd.VehicleType, true, out var vt) && vt != vehicle.Type)
        {
            vehicle.Type  = vt;
            retrigger     = true;
        }

        if (retrigger) vehicle.PendingReview = true; // re-triggers review

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.DriverSuspend,
            "Driver", cmd.DriverId, reason: "Vehicle details corrected",
            before: before, after: new { vehicle.Plate, vehicle.Type, PendingReview = retrigger }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── A08: POST /admin/drivers/{id}/reset-performance ──────────────────────────
// metric, reason. Rare, high-privilege, heavily audited.

public record ResetDriverPerformanceCommand(string DriverId, string Metric, string Reason,
    string StaffId, string StaffName) : IRequest<DriverCommandResult>;

public class ResetDriverPerformanceHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<ResetDriverPerformanceCommand, DriverCommandResult>
{
    public async Task<DriverCommandResult> Handle(ResetDriverPerformanceCommand cmd, CancellationToken ct)
    {
        var driver = await db.DriverProfiles.FirstOrDefaultAsync(d => d.Id == cmd.DriverId, ct);
        if (driver is null) return new(false, "DRIVER_NOT_FOUND");

        object before;
        switch (cmd.Metric.ToLower())
        {
            case "acceptance_rate":
                before = new { driver.AcceptanceRate };
                driver.AcceptanceRate = 1.0m;
                break;
            case "cancellation_rate":
                before = new { driver.CancellationRate };
                driver.CancellationRate = 0m;
                break;
            case "completion_rate":
                before = new { driver.CompletionRate };
                driver.CompletionRate = 1.0m;
                break;
            default:
                return new(false, "INVALID_METRIC");
        }

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.DriverPerformanceReset,
            "Driver", cmd.DriverId, cmd.Reason,
            before, new { Metric = cmd.Metric, Reset = true }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── A20: PATCH /admin/drivers/{id} ───────────────────────────────────────────
// Identity fields. Name or ID number change must re-open the identity step.

public record UpdateDriverCommand(string DriverId, string? FirstName, string? LastName,
    string? Email, string? Phone, string StaffId, string StaffName) : IRequest<DriverCommandResult>;

public class UpdateDriverHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UpdateDriverCommand, DriverCommandResult>
{
    public async Task<DriverCommandResult> Handle(UpdateDriverCommand cmd, CancellationToken ct)
    {
        var driver = await db.DriverProfiles
            .Include(d => d.User)
            .Include(d => d.Onboarding)
            .FirstOrDefaultAsync(d => d.Id == cmd.DriverId, ct);
        if (driver is null) return new(false, "DRIVER_NOT_FOUND");

        var before = new { driver.User.FirstName, driver.User.LastName, driver.User.Email, driver.User.Phone };
        bool nameChanged = false;

        if (cmd.FirstName is not null && cmd.FirstName != driver.User.FirstName)
        { driver.User.FirstName = cmd.FirstName; nameChanged = true; }

        if (cmd.LastName is not null && cmd.LastName != driver.User.LastName)
        { driver.User.LastName = cmd.LastName; nameChanged = true; }

        if (cmd.Email    is not null) driver.User.Email = cmd.Email;
        if (cmd.Phone    is not null) { driver.User.Phone = cmd.Phone; driver.User.PhoneVerified = false; }

        // Name change must re-open the identity step
        if (nameChanged && driver.Onboarding is not null)
        {
            driver.Onboarding.IdentityStatus = OnboardingStepStatus.Submitted; // re-opens for review
        }

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.DriverSuspend,
            "Driver", cmd.DriverId, reason: "Admin edited driver profile",
            before: before, after: new { cmd.FirstName, cmd.LastName, cmd.Email, IdentityReopened = nameChanged }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── A20: POST /admin/drivers/{id}/password/reset-link ────────────────────────

public record SendDriverPasswordResetLinkCommand(string DriverId, string StaffId, string StaffName)
    : IRequest<DriverCommandResult>;

public class SendDriverPasswordResetLinkHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<SendDriverPasswordResetLinkCommand, DriverCommandResult>
{
    public async Task<DriverCommandResult> Handle(SendDriverPasswordResetLinkCommand cmd, CancellationToken ct)
    {
        var driver = await db.DriverProfiles
            .Select(d => new { d.Id, d.User.Phone })
            .FirstOrDefaultAsync(d => d.Id == cmd.DriverId, ct);
        if (driver is null) return new(false, "DRIVER_NOT_FOUND");

        db.OtpRecords.Add(new Domain.Entities.OtpRecord
        {
            Identifier = driver.Phone,
            OtpToken   = Guid.NewGuid().ToString("N"),
            CodeHash   = string.Empty,
            Purpose    = OtpPurpose.VerifyPhone,
            Role       = "driver",
            ExpiresAt  = DateTime.UtcNow.AddMinutes(30)
        });

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.PasswordResetLinkSent,
            "Driver", cmd.DriverId, reason: "Admin initiated password reset", ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── A20: POST /admin/drivers/{id}/sessions/revoke ────────────────────────────
// Also forces driver offline — revoked session must not keep receiving offers.

public record RevokeDriverSessionsCommand(string DriverId, string StaffId, string StaffName)
    : IRequest<DriverCommandResult>;

public class RevokeDriverSessionsHandler(IApplicationDbContext db, IAuditService audit,
    IRealtimeService realtime) : IRequestHandler<RevokeDriverSessionsCommand, DriverCommandResult>
{
    public async Task<DriverCommandResult> Handle(RevokeDriverSessionsCommand cmd, CancellationToken ct)
    {
        var driver = await db.DriverProfiles
            .Select(d => new { d.Id, d.UserId })
            .FirstOrDefaultAsync(d => d.Id == cmd.DriverId, ct);
        if (driver is null) return new(false, "DRIVER_NOT_FOUND");

        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == driver.UserId && !t.IsRevoked).ToListAsync(ct);
        foreach (var t in tokens) t.IsRevoked = true;

        // Force offline — revoked session must not keep receiving offers
        var profile = await db.DriverProfiles.FirstOrDefaultAsync(d => d.Id == cmd.DriverId, ct);
        if (profile is not null) profile.IsOnline = false;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.DriverLogout,
            "Driver", cmd.DriverId, reason: "Admin revoked all driver sessions",
            after: new { sessions_revoked = tokens.Count, forced_offline = true }, ct: ct);
        await db.SaveChangesAsync(ct);

        await realtime.PublishToDriverAsync(cmd.DriverId, "driver.forced_offline",
            new { reason = "session_revoked" }, ct);

        return new(true, null);
    }
}

// ── A20: POST /admin/drivers/{id}/block ──────────────────────────────────────
// reason, settle_cash. Forces offline, cancels active job.
// Returns outstanding cash owed and wallet balance.

public record BlockDriverCommand(string DriverId, string Reason, bool SettleCash,
    string StaffId, string StaffName) : IRequest<DriverCommandResult>;

public class BlockDriverHandler(IApplicationDbContext db, IAuditService audit,
    IRealtimeService realtime) : IRequestHandler<BlockDriverCommand, DriverCommandResult>
{
    public async Task<DriverCommandResult> Handle(BlockDriverCommand cmd, CancellationToken ct)
    {
        var driver = await db.DriverProfiles
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == cmd.DriverId, ct);
        if (driver is null) return new(false, "DRIVER_NOT_FOUND");

        var before = new { driver.User.Status, driver.IsOnline };
        driver.User.Status           = UserStatus.Blocked;
        driver.User.SuspensionReason = cmd.Reason;
        driver.IsOnline        = false;

        // Cancel active job with driver-fault handling
        await DriverHelpers.CancelActiveJobAsync(db, cmd.DriverId, cmd.StaffId, ct);

        // Revoke all sessions
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == driver.UserId && !t.IsRevoked).ToListAsync(ct);
        foreach (var t in tokens) t.IsRevoked = true;

        // Report stranded values
        var driverWallet = await db.DriverWallets
            .FirstOrDefaultAsync(w => w.DriverId == cmd.DriverId, ct);
        var cashOwed     = driverWallet?.PendingCashSettlement ?? 0L;
        var balance      = driverWallet?.AvailableBalance      ?? 0L;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.DriverBlock,
            "Driver", cmd.DriverId, cmd.Reason, before,
            new { Status = "Blocked", IsOnline = false }, ct: ct);
        await db.SaveChangesAsync(ct);

        await realtime.PublishToDriverAsync(cmd.DriverId, "driver.forced_offline",
            new { reason = cmd.Reason }, ct);

        return new(true, null, new { cash_owed = cashOwed, wallet_balance = balance });
    }
}

// ── A20: POST /admin/drivers/{id}/unblock ────────────────────────────────────
// reason. Re-checks document expiry before letting them online again.

public record UnblockDriverCommand(string DriverId, string Reason, string StaffId, string StaffName)
    : IRequest<DriverCommandResult>;

public class UnblockDriverHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UnblockDriverCommand, DriverCommandResult>
{
    public async Task<DriverCommandResult> Handle(UnblockDriverCommand cmd, CancellationToken ct)
    {
        var driver = await db.DriverProfiles
            .Include(d => d.User)
            .FirstOrDefaultAsync(d => d.Id == cmd.DriverId, ct);
        if (driver is null) return new(false, "DRIVER_NOT_FOUND");

        var before = new { driver.User.Status };
        driver.User.Status           = UserStatus.Active;
        driver.User.SuspensionReason = null;

        // Re-check document expiry — expired docs block going online
        var now            = DateTime.UtcNow;
        var expiredDocs    = await db.DriverDocuments
            .Where(d => d.DriverId == cmd.DriverId
                     && d.Status == OnboardingStepStatus.Approved
                     && d.ExpiresAt.HasValue && d.ExpiresAt.Value < now)
            .Select(d => d.Type.ToString())
            .ToListAsync(ct);

        var canGoOnline = !expiredDocs.Any();
        // Driver stays offline if docs are expired — they must renew first

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.DriverUnblock,
            "Driver", cmd.DriverId, cmd.Reason, before,
            new { Status = "Active", CanGoOnline = canGoOnline, ExpiredDocs = expiredDocs }, ct: ct);
        await db.SaveChangesAsync(ct);

        return new(true, null, new { can_go_online = canGoOnline, expired_docs = expiredDocs });
    }
}
