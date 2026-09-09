using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Trips.Commands;

// ── DTOs ─────────────────────────────────────────────────────────────────────

public record TripCommandResult(bool Success, string? ErrorCode);

// ── POST /admin/trips/{id}/refund ─────────────────────────────────────────────
// amount?, reason, destination=original|wallet, Idempotency-Key
// Permission: trips.refund
// recover_from_driver: whether to claw back driver earnings

public record RefundTripCommand(
    string TripId,
    long? Amount,
    string Reason,
    string Destination,   // original | wallet
    bool RecoverFromDriver,
    string IdempotencyKey,
    string StaffId, string StaffName, string Market) : IRequest<TripCommandResult>;

public class RefundTripHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<RefundTripCommand, TripCommandResult>
{
    public async Task<TripCommandResult> Handle(RefundTripCommand cmd, CancellationToken ct)
    {
        // Idempotency check
        var existing = await db.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.Key == cmd.IdempotencyKey && r.Endpoint == "admin.trip.refund", ct);
        if (existing is not null) return new(true, null); // replay safe

        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == cmd.TripId, ct);
        if (trip is null) return new(false, "TRIP_NOT_FOUND");
        if (trip.JobState != JobState.Completed) return new(false, "TRIP_NOT_COMPLETED");

        var refundAmount = cmd.Amount ?? trip.FareGross;
        if (refundAmount > trip.FareGross || refundAmount <= 0)
            return new(false, "INVALID_REFUND_AMOUNT");

        var before = new { trip.FareGross, trip.DriverEarnings };

        if (cmd.Destination == "wallet")
        {
            // Credit rider wallet
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == trip.RiderId, ct);
            if (wallet is null) return new(false, "WALLET_NOT_FOUND");

            wallet.Balance += refundAmount;
            db.WalletTransactions.Add(new Domain.Entities.WalletTransaction
            {
                WalletId    = wallet.Id,
                Type        = WalletTransactionType.Refund,
                Amount      = refundAmount,
                BalanceAfter = wallet.Balance,
                Title       = $"Refund — Trip {trip.Code}",
                Subtitle    = cmd.Reason,
                ReferenceId = trip.Id,
                Reason      = cmd.Reason,
                CreatedBy   = cmd.StaffId
            });
        }
        // For destination=original: initiating a gateway reversal would be done here
        // via IPaymentGateway.RefundAsync() — deferred to payment integration phase

        // Recover from driver if requested
        if (cmd.RecoverFromDriver && trip.DriverId is not null)
        {
            var driverWallet = await db.DriverWallets
                .FirstOrDefaultAsync(w => w.DriverId == trip.DriverId, ct);
            if (driverWallet is not null)
            {
                var clawback = Math.Min(refundAmount, trip.DriverEarnings);
                driverWallet.AvailableBalance -= clawback;
                db.DriverWalletTransactions.Add(new Domain.Entities.DriverWalletTransaction
                {
                    DriverWalletId = driverWallet.Id,
                    Type           = "clawback",
                    Amount         = -clawback,
                    BalanceAfter   = driverWallet.AvailableBalance,
                    ReferenceId    = trip.Id,
                    Reason         = $"Admin refund clawback: {cmd.Reason}",
                    CreatedBy      = cmd.StaffId
                });
            }
        }

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.TripRefund,
            "Trip", trip.Id, cmd.Reason,
            before, new { refundAmount, cmd.Destination, cmd.RecoverFromDriver },
            market: cmd.Market, ct: ct);

        // Save idempotency record
        db.IdempotencyRecords.Add(new Domain.Entities.IdempotencyRecord
        {
            Key        = cmd.IdempotencyKey,
            Endpoint   = "admin.trip.refund",
            ResponseJson = $"{{\"trip_id\":\"{cmd.TripId}\",\"amount\":{refundAmount}}}",
            StatusCode = 200,
            ExpiresAt  = DateTime.UtcNow.AddDays(7)
        });

        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/trips/{id}/adjust-fare ───────────────────────────────────────
// new_total, reason — recomputes commission and driver earnings; writes both ledger legs

public record AdjustFareCommand(
    string TripId,
    long NewTotal,
    string Reason,
    string StaffId, string StaffName, string Market) : IRequest<TripCommandResult>;

public class AdjustFareHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<AdjustFareCommand, TripCommandResult>
{
    public async Task<TripCommandResult> Handle(AdjustFareCommand cmd, CancellationToken ct)
    {
        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == cmd.TripId, ct);
        if (trip is null) return new(false, "TRIP_NOT_FOUND");
        if (trip.JobState != JobState.Completed) return new(false, "TRIP_NOT_COMPLETED");
        if (cmd.NewTotal <= 0) return new(false, "INVALID_FARE");

        var before = new
        {
            trip.FareGross, trip.CommissionAmount, trip.DriverEarnings, trip.WalletCredit
        };

        var oldGross        = trip.FareGross;
        var newCommission   = (long)(cmd.NewTotal * trip.CommissionRate);
        var newEarnings     = cmd.NewTotal - newCommission;
        var diff            = cmd.NewTotal - oldGross;

        trip.FareGross        = cmd.NewTotal;
        trip.CommissionAmount = newCommission;
        trip.DriverEarnings   = newEarnings;
        trip.FareIsFinal      = true;

        // Ledger leg 1: Rider wallet adjustment
        if (trip.PaymentMethod == PaymentMethod.Wallet && diff != 0)
        {
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == trip.RiderId, ct);
            if (wallet is not null)
            {
                wallet.Balance  -= diff; // negative diff = credit; positive = charge
                trip.WalletCredit = Math.Max(0, trip.WalletCredit - diff);
                db.WalletTransactions.Add(new Domain.Entities.WalletTransaction
                {
                    WalletId    = wallet.Id,
                    Type        = WalletTransactionType.Adjustment,
                    Amount      = -diff,
                    BalanceAfter = wallet.Balance,
                    Title       = $"Fare adjustment — Trip {trip.Code}",
                    Subtitle    = cmd.Reason,
                    ReferenceId = trip.Id,
                    Reason      = cmd.Reason,
                    CreatedBy   = cmd.StaffId
                });
            }
        }

        // Ledger leg 2: Driver wallet adjustment
        if (trip.DriverId is not null)
        {
            var driverWallet = await db.DriverWallets
                .FirstOrDefaultAsync(w => w.DriverId == trip.DriverId, ct);
            if (driverWallet is not null)
            {
                var earningsDiff = newEarnings - before.DriverEarnings;
                driverWallet.AvailableBalance += earningsDiff;
                db.DriverWalletTransactions.Add(new Domain.Entities.DriverWalletTransaction
                {
                    DriverWalletId = driverWallet.Id,
                    Type           = "fare_adjustment",
                    Amount         = earningsDiff,
                    BalanceAfter   = driverWallet.AvailableBalance,
                    ReferenceId    = trip.Id,
                    Reason         = $"Fare adjustment: {cmd.Reason}",
                    CreatedBy      = cmd.StaffId
                });
            }
        }

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.TripAdjust,
            "Trip", trip.Id, cmd.Reason, before,
            new { NewTotal = cmd.NewTotal, newCommission, newEarnings },
            market: cmd.Market, ct: ct);

        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/trips/{id}/reopen ─────────────────────────────────────────────
// Moves to disputed and links a ticket

public record ReopenTripCommand(
    string TripId, string StaffId, string StaffName, string Market) : IRequest<TripCommandResult>;

public class ReopenTripHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<ReopenTripCommand, TripCommandResult>
{
    public async Task<TripCommandResult> Handle(ReopenTripCommand cmd, CancellationToken ct)
    {
        var trip = await db.Trips.FirstOrDefaultAsync(t => t.Id == cmd.TripId, ct);
        if (trip is null) return new(false, "TRIP_NOT_FOUND");
        if (trip.JobState == JobState.Disputed) return new(false, "TRIP_ALREADY_DISPUTED");

        var before = new { trip.JobState };
        trip.JobState = JobState.Disputed;

        // Create a support ticket linked to this trip
        var ticket = new Domain.Entities.SupportTicket
        {
            UserId      = trip.RiderId,
            UserRole    = "rider",
            Category    = "dispute",
            Description = $"Trip {trip.Code} reopened for dispute by admin",
            TripId      = trip.Id,
            Status      = TicketStatus.Open,
            Priority    = TicketPriority.High,
            Reference   = $"DISP-{trip.Code}",
            Market      = trip.Market
        };
        db.SupportTickets.Add(ticket);

        db.TripStateHistories.Add(new Domain.Entities.TripStateHistory
        {
            TripId     = trip.Id,
            State      = JobState.Disputed,
            OccurredAt = DateTime.UtcNow,
            Actor      = "admin",
            ActorId    = cmd.StaffId
        });

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.TripReopen,
            "Trip", trip.Id, reason: "Reopened for dispute",
            before: before, after: new { JobState = "Disputed" },
            market: cmd.Market, ct: ct);

        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}
