using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Auth.Commands;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Profile.Commands;

public record CloseAccountCommand(
    string UserId,
    string Reason,
    string? PasswordOrOtp
) : IRequest;

public class CloseAccountHandler(IApplicationDbContext db, IPasswordHasher hasher)
    : IRequestHandler<CloseAccountCommand>
{
    public async Task Handle(CloseAccountCommand req, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([req.UserId], ct)
            ?? throw new KeyNotFoundException("User not found.");

        // Verify identity — password takes priority; fall back to OTP code
        if (!string.IsNullOrWhiteSpace(req.PasswordOrOtp))
        {
            var byPassword = !string.IsNullOrWhiteSpace(user.PasswordHash) &&
                             hasher.Verify(req.PasswordOrOtp, user.PasswordHash);

            if (!byPassword)
            {
                // Treat as an OTP code: hash it and look for a matching, unused OTP record
                var codeHash = RequestOtpHandler.HashCode(req.PasswordOrOtp);
                var otpValid = await db.OtpRecords.AnyAsync(o =>
                    o.Identifier == user.Phone &&
                    o.CodeHash == codeHash &&
                    !o.IsUsed &&
                    o.ExpiresAt > DateTime.UtcNow, ct);

                if (!otpValid)
                    throw new UnauthorizedAccessException(
                        "Invalid password or OTP. Request a fresh code via /auth/otp/request first.");

                // Consume the OTP
                var otpRecord = await db.OtpRecords
                    .Where(o => o.Identifier == user.Phone &&
                                o.CodeHash == codeHash &&
                                !o.IsUsed &&
                                o.ExpiresAt > DateTime.UtcNow)
                    .FirstAsync(ct);
                otpRecord.IsUsed = true;
            }
        }

        // Block if wallet balance exists
        var wallet = await db.Wallets
            .Where(w => w.UserId == req.UserId)
            .Select(w => new { w.Balance, w.Currency })
            .FirstOrDefaultAsync(ct);

        if (wallet?.Balance > 0)
            throw new InvalidOperationException(
                $"CONFLICT: Wallet balance of {wallet.Balance} {wallet.Currency} must be " +
                "withdrawn before closing your account.");

        // Block if driver has pending cash settlement
        if (user.Role == UserRole.Driver)
        {
            var driverWallet = await db.DriverWallets
                .Where(w => w.DriverId == req.UserId)
                .Select(w => new { w.PendingCashSettlement, w.Currency })
                .FirstOrDefaultAsync(ct);

            if (driverWallet?.PendingCashSettlement > 0)
                throw new InvalidOperationException(
                    $"CONFLICT: Pending cash settlement of {driverWallet.PendingCashSettlement} " +
                    $"{driverWallet.Currency} must be settled before closing your account.");
        }

        // Block if active trip in progress
        var hasActiveTrip = await db.Trips.AnyAsync(t =>
            (t.RiderId == req.UserId || t.DriverId == req.UserId) &&
            t.JobState != JobState.Completed &&
            t.JobState != JobState.CancelledByRider &&
            t.JobState != JobState.CancelledByDriver &&
            t.JobState != JobState.CancelledByAdmin &&
            t.JobState != JobState.Expired &&
            t.JobState != JobState.RejectedByDriver, ct);

        if (hasActiveTrip)
            throw new InvalidOperationException(
                "CONFLICT: Cannot close account while a trip is in progress.");

        user.DeletionRequested = true;
        user.DeletionRequestedAt = DateTime.UtcNow;
        user.DeletionReason = req.Reason;

        // Revoke all sessions immediately
        var tokens = await db.RefreshTokens
            .Where(t => t.UserId == req.UserId && !t.IsRevoked)
            .ToListAsync(ct);
        foreach (var t in tokens) t.IsRevoked = true;

        await db.SaveChangesAsync(ct);
    }
}
