using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Riders.Commands;

public record RiderCommandResult(bool Success, string? ErrorCode, object? Data = null);

// ── A07: POST /admin/riders/{id}/suspend  (action=suspend|reinstate) ──────────

public record SuspendRiderCommand(string RiderId, string Action, string Reason,
    DateTime? Until, string StaffId, string StaffName) : IRequest<RiderCommandResult>;

public class SuspendRiderHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<SuspendRiderCommand, RiderCommandResult>
{
    public async Task<RiderCommandResult> Handle(SuspendRiderCommand cmd, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Id == cmd.RiderId && u.Role == UserRole.Rider, ct);
        if (user is null) return new(false, "RIDER_NOT_FOUND");

        var before = new { user.Status, user.SuspensionReason, user.SuspendedUntil };

        if (cmd.Action == "reinstate")
        {
            user.Status          = UserStatus.Active;
            user.SuspensionReason = null;
            user.SuspendedUntil  = null;
            await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.RiderSuspend,
                "Rider", cmd.RiderId, cmd.Reason, before, new { Status = "Active" }, ct: ct);
        }
        else
        {
            user.Status           = UserStatus.Suspended;
            user.SuspensionReason = cmd.Reason; // shown in app on next launch
            user.SuspendedUntil   = cmd.Until;
            await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.RiderSuspend,
                "Rider", cmd.RiderId, cmd.Reason, before,
                new { Status = "Suspended", cmd.Until }, ct: ct);
        }

        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── A07: POST /admin/riders/{id}/flag ─────────────────────────────────────────

public record FlagRiderCommand(string RiderId, string Reason, string Severity,
    string StaffId, string StaffName) : IRequest<RiderCommandResult>;

public class FlagRiderHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<FlagRiderCommand, RiderCommandResult>
{
    public async Task<RiderCommandResult> Handle(FlagRiderCommand cmd, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Id == cmd.RiderId && u.Role == UserRole.Rider, ct);
        if (user is null) return new(false, "RIDER_NOT_FOUND");

        var before = new { user.IsFlagged, user.FlagReason, user.FlagSeverity };
        user.IsFlagged   = true;
        user.FlagReason  = cmd.Reason;
        user.FlagSeverity = cmd.Severity;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.RiderFlag,
            "Rider", cmd.RiderId, cmd.Reason, before,
            new { IsFlagged = true, cmd.Severity }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── A07: POST /admin/riders/{id}/logout-all ───────────────────────────────────

public record LogoutRiderAllCommand(string RiderId, string StaffId, string StaffName)
    : IRequest<RiderCommandResult>;

public class LogoutRiderAllHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<LogoutRiderAllCommand, RiderCommandResult>
{
    public async Task<RiderCommandResult> Handle(LogoutRiderAllCommand cmd, CancellationToken ct)
    {
        var exists = await db.Users.AnyAsync(u => u.Id == cmd.RiderId, ct);
        if (!exists) return new(false, "RIDER_NOT_FOUND");

        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == cmd.RiderId && !t.IsRevoked)
            .ToListAsync(ct);

        foreach (var token in tokens) token.IsRevoked = true;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.RiderLogout,
            "Rider", cmd.RiderId, reason: "Admin forced logout of all sessions",
            after: new { sessions_revoked = tokens.Count }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── A07: DELETE /admin/riders/{id} ────────────────────────────────────────────
// Anonymise — trips and ledger survive. 409 if wallet balance or open trip exists.

public record DeleteRiderCommand(string RiderId, string StaffId, string StaffName)
    : IRequest<RiderCommandResult>;

public class DeleteRiderHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<DeleteRiderCommand, RiderCommandResult>
{
    private static readonly JobState[] ActiveStates =
    [
        JobState.Broadcasting, JobState.Offered, JobState.Accepted,
        JobState.EnRouteToPickup, JobState.ArrivedAtPickup,
        JobState.PickedUp, JobState.EnRouteToDropoff, JobState.ArrivedAtDropoff
    ];

    public async Task<RiderCommandResult> Handle(DeleteRiderCommand cmd, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == cmd.RiderId, ct);
        if (user is null) return new(false, "RIDER_NOT_FOUND");

        // 409 if wallet balance > 0
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == cmd.RiderId, ct);
        if (wallet is not null && wallet.Balance > 0)
            return new(false, "WALLET_BALANCE_EXISTS");

        // 409 if open trip
        var hasOpenTrip = await db.Trips.AnyAsync(
            t => t.RiderId == cmd.RiderId && ActiveStates.Contains(t.JobState), ct);
        if (hasOpenTrip) return new(false, "OPEN_TRIP_EXISTS");

        // Anonymise: trips and ledger entries survive for accounting
        var before = new { user.FirstName, user.LastName, user.Phone, user.Email };
        user.FirstName         = "Deleted";
        user.LastName          = "User";
        user.Phone             = $"+00000000{cmd.RiderId[^4..]}"; // unique placeholder
        user.Email             = null;
        user.PhotoUrl          = null;
        user.PasswordHash      = null;
        user.DeletionRequested = true;
        user.DeletionRequestedAt = DateTime.UtcNow;
        user.Status            = UserStatus.Blocked;

        // Revoke all sessions
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == cmd.RiderId).ToListAsync(ct);
        foreach (var t in tokens) t.IsRevoked = true;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.RiderDelete,
            "Rider", cmd.RiderId, reason: "Account anonymised per deletion request",
            before: before, after: new { anonymised_at = DateTime.UtcNow }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── A20: PATCH /admin/riders/{id} ─────────────────────────────────────────────
// Edit details. Phone change requires OTP (we update pending state; actual change after OTP).

public record UpdateRiderCommand(string RiderId, string? FirstName, string? LastName,
    string? Email, string? Phone, string? Language,
    string StaffId, string StaffName) : IRequest<RiderCommandResult>;

public class UpdateRiderHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UpdateRiderCommand, RiderCommandResult>
{
    public async Task<RiderCommandResult> Handle(UpdateRiderCommand cmd, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Id == cmd.RiderId && u.Role == UserRole.Rider, ct);
        if (user is null) return new(false, "RIDER_NOT_FOUND");

        var before = new { user.FirstName, user.LastName, user.Email, user.Phone, user.Language };

        if (cmd.FirstName  is not null) user.FirstName  = cmd.FirstName;
        if (cmd.LastName   is not null) user.LastName   = cmd.LastName;
        if (cmd.Email      is not null) user.Email      = cmd.Email;
        if (cmd.Language   is not null) user.Language   = cmd.Language;

        // Phone change: update and mark as pending OTP verification
        if (cmd.Phone is not null && cmd.Phone != user.Phone)
        {
            user.Phone         = cmd.Phone;
            user.PhoneVerified = false; // requires re-verification
        }

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.RiderSuspend,
            "Rider", cmd.RiderId, reason: "Admin edited rider profile",
            before: before, after: new { cmd.FirstName, cmd.LastName, cmd.Email, cmd.Phone }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── A20: POST /admin/riders/{id}/password/reset-link ─────────────────────────
// Sends app reset flow. Admins never set the password directly.

public record SendRiderPasswordResetLinkCommand(string RiderId, string StaffId, string StaffName)
    : IRequest<RiderCommandResult>;

public class SendRiderPasswordResetLinkHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<SendRiderPasswordResetLinkCommand, RiderCommandResult>
{
    public async Task<RiderCommandResult> Handle(SendRiderPasswordResetLinkCommand cmd, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(
            u => u.Id == cmd.RiderId && u.Role == UserRole.Rider, ct);
        if (user is null) return new(false, "RIDER_NOT_FOUND");

        // Generate a reset OTP record using existing OTP flow
        var otp = new Domain.Entities.OtpRecord
        {
            Identifier  = user.Phone,
            OtpToken    = Guid.NewGuid().ToString("N"),
            CodeHash    = string.Empty, // app will send via OTP service
            Purpose     = OtpPurpose.VerifyPhone,
            Role        = "rider",
            ExpiresAt   = DateTime.UtcNow.AddMinutes(30)
        };
        db.OtpRecords.Add(otp);

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.PasswordResetLinkSent,
            "Rider", cmd.RiderId, reason: "Admin initiated password reset", ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── A20: POST /admin/riders/{id}/sessions/revoke ──────────────────────────────

public record RevokeRiderSessionsCommand(string RiderId, string StaffId, string StaffName)
    : IRequest<RiderCommandResult>;

public class RevokeRiderSessionsHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<RevokeRiderSessionsCommand, RiderCommandResult>
{
    public async Task<RiderCommandResult> Handle(RevokeRiderSessionsCommand cmd, CancellationToken ct)
    {
        var exists = await db.Users.AnyAsync(u => u.Id == cmd.RiderId, ct);
        if (!exists) return new(false, "RIDER_NOT_FOUND");

        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == cmd.RiderId && !t.IsRevoked).ToListAsync(ct);
        foreach (var t in tokens) t.IsRevoked = true;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.RiderLogout,
            "Rider", cmd.RiderId, reason: "Admin revoked all rider sessions",
            after: new { sessions_revoked = tokens.Count }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── A20: POST /admin/riders/{id}/block ───────────────────────────────────────
// reason (shown in app), cancel_active_trip
// Returns whether wallet balance is stranded

public record BlockRiderCommand(string RiderId, string Reason, bool CancelActiveTrip,
    string StaffId, string StaffName) : IRequest<RiderCommandResult>;

public class BlockRiderHandler(IApplicationDbContext db, IAuditService audit,
    IRealtimeService realtime) : IRequestHandler<BlockRiderCommand, RiderCommandResult>
{
    private static readonly JobState[] ActiveStates =
    [
        JobState.Broadcasting, JobState.Offered, JobState.Accepted,
        JobState.EnRouteToPickup, JobState.ArrivedAtPickup,
        JobState.PickedUp, JobState.EnRouteToDropoff, JobState.ArrivedAtDropoff
    ];

    public async Task<RiderCommandResult> Handle(BlockRiderCommand cmd, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == cmd.RiderId, ct);
        if (user is null) return new(false, "RIDER_NOT_FOUND");

        var before = new { user.Status };
        user.Status           = UserStatus.Blocked;
        user.SuspensionReason = cmd.Reason; // shown in their app

        // Revoke all sessions
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == cmd.RiderId && !t.IsRevoked).ToListAsync(ct);
        foreach (var t in tokens) t.IsRevoked = true;

        // Check wallet balance (returned in response so operator can refund it)
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == cmd.RiderId, ct);
        var walletBalance = wallet?.Balance ?? 0L;

        // Optionally cancel active trip
        if (cmd.CancelActiveTrip)
        {
            var activeTrip = await db.Trips.FirstOrDefaultAsync(
                t => t.RiderId == cmd.RiderId && ActiveStates.Contains(t.JobState), ct);
            if (activeTrip is not null)
            {
                activeTrip.JobState    = JobState.CancelledByAdmin;
                activeTrip.CancelledAt = DateTime.UtcNow;
                db.TripStateHistories.Add(new Domain.Entities.TripStateHistory
                {
                    TripId     = activeTrip.Id, State = JobState.CancelledByAdmin,
                    OccurredAt = DateTime.UtcNow, Actor = "admin", ActorId = cmd.StaffId
                });
                if (activeTrip.DriverId is not null)
                    await realtime.PublishToDriverAsync(activeTrip.DriverId, "job.updated",
                        new { job_id = activeTrip.Id, state = "cancelled_by_admin" }, ct);
            }
        }

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.RiderBlock,
            "Rider", cmd.RiderId, cmd.Reason, before, new { Status = "Blocked" }, ct: ct);
        await db.SaveChangesAsync(ct);

        return new(true, null, new { wallet_balance_stranded = walletBalance > 0, wallet_balance = walletBalance });
    }
}

// ── A20: POST /admin/riders/{id}/unblock ─────────────────────────────────────

public record UnblockRiderCommand(string RiderId, string Reason, string StaffId, string StaffName)
    : IRequest<RiderCommandResult>;

public class UnblockRiderHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<UnblockRiderCommand, RiderCommandResult>
{
    public async Task<RiderCommandResult> Handle(UnblockRiderCommand cmd, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == cmd.RiderId, ct);
        if (user is null) return new(false, "RIDER_NOT_FOUND");

        var before     = new { user.Status };
        user.Status           = UserStatus.Active;
        user.SuspensionReason = null;

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.RiderUnblock,
            "Rider", cmd.RiderId, cmd.Reason, before, new { Status = "Active" }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}
