using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Admin.Growth.Commands;

public record GrowthCommandResult(bool Success, string? ErrorCode, object? Data = null);

// ── POST /admin/co-ride/listings/{id}/cancel ──────────────────────────────────
// reason, refund_passengers=true. Notifies everyone booked.

public record CancelCoRideListingCommand(string ListingId, string Reason, bool RefundPassengers,
    string StaffId, string StaffName) : IRequest<GrowthCommandResult>;

public class CancelCoRideListingHandler(IApplicationDbContext db, IAuditService audit,
    IPushService push, IRealtimeService realtime)
    : IRequestHandler<CancelCoRideListingCommand, GrowthCommandResult>
{
    public async Task<GrowthCommandResult> Handle(CancelCoRideListingCommand cmd, CancellationToken ct)
    {
        var listing = await db.CoRideListings
            .Include(l => l.Bookings)
            .FirstOrDefaultAsync(l => l.Id == cmd.ListingId, ct);

        if (listing is null) return new(false, "LISTING_NOT_FOUND");
        if (listing.Status is "cancelled" or "departed")
            return new(false, "LISTING_NOT_CANCELLABLE");

        var before = new { listing.Status };
        listing.Status             = "cancelled";
        listing.CancellationReason = cmd.Reason;

        var riderIds = listing.Bookings.Select(b => b.RiderId).ToList();

        if (cmd.RefundPassengers)
        {
            foreach (var booking in listing.Bookings.Where(b =>
                b.Status == CoRideBookingStatus.Upcoming))
            {
                // Refund to rider wallet
                var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == booking.RiderId, ct);
                if (wallet is not null)
                {
                    wallet.Balance += booking.Total;
                    db.WalletTransactions.Add(new Domain.Entities.WalletTransaction
                    {
                        WalletId    = wallet.Id,
                        Type        = WalletTransactionType.Refund,
                        Amount      = booking.Total,
                        BalanceAfter = wallet.Balance,
                        Title       = "Co-ride listing cancelled",
                        Subtitle    = cmd.Reason,
                        ReferenceId = booking.Id,
                        Reason      = cmd.Reason,
                        CreatedBy   = cmd.StaffId
                    });
                }
                booking.Status = CoRideBookingStatus.Cancelled;
                booking.CancellationReason = cmd.Reason;
            }
        }

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.TripAdjust,
            "CoRideListing", cmd.ListingId, cmd.Reason, before,
            new { Status = "cancelled", RefundPassengers = cmd.RefundPassengers }, ct: ct);
        await db.SaveChangesAsync(ct);

        // Notify all booked passengers
        if (riderIds.Any())
        {
            var tokens = await db.UserDevices
                .Where(d => riderIds.Contains(d.UserId) && d.FcmToken != null)
                .Select(d => d.FcmToken!)
                .Distinct().ToListAsync(ct);

            if (tokens.Any())
                await push.SendBatchAsync(tokens, "Co-ride Cancelled",
                    $"Your co-ride listing has been cancelled. Reason: {cmd.Reason}",
                    "coride_cancelled", ct: ct);

            foreach (var riderId in riderIds)
                await realtime.PublishToUserAsync(riderId, "ride.status_changed",
                    new { listing_id = cmd.ListingId, status = "cancelled" }, ct);
        }

        return new(true, null);
    }
}

// ── POST /admin/co-ride/bookings/{id}/refund ──────────────────────────────────
// Releases the seat back to inventory in the same transaction.

public record RefundCoRideBookingCommand(string BookingId, string StaffId, string StaffName)
    : IRequest<GrowthCommandResult>;

public class RefundCoRideBookingHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<RefundCoRideBookingCommand, GrowthCommandResult>
{
    public async Task<GrowthCommandResult> Handle(RefundCoRideBookingCommand cmd, CancellationToken ct)
    {
        var booking = await db.CoRideBookings
            .Include(b => b.Listing)
            .FirstOrDefaultAsync(b => b.Id == cmd.BookingId, ct);

        if (booking is null) return new(false, "BOOKING_NOT_FOUND");
        if (booking.Status != CoRideBookingStatus.Upcoming)
            return new(false, "BOOKING_NOT_REFUNDABLE");

        var before = new { booking.Status, booking.Listing.SeatsTaken };

        // Release seat back to inventory in the same transaction
        booking.Status             = CoRideBookingStatus.Cancelled;
        booking.Listing.SeatsTaken = Math.Max(0, booking.Listing.SeatsTaken - booking.Seats);

        // Refund to rider wallet
        var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == booking.RiderId, ct);
        if (wallet is not null)
        {
            wallet.Balance += booking.Total;
            db.WalletTransactions.Add(new Domain.Entities.WalletTransaction
            {
                WalletId    = wallet.Id,
                Type        = WalletTransactionType.Refund,
                Amount      = booking.Total,
                BalanceAfter = wallet.Balance,
                Title       = "Co-ride seat refund",
                ReferenceId = booking.Id,
                CreatedBy   = cmd.StaffId
            });
        }

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.TripRefund,
            "CoRideBooking", cmd.BookingId, reason: "Admin refunded co-ride booking",
            before: before, after: new { Status = "Cancelled", SeatReleased = booking.Seats }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null);
    }
}

// ── POST /admin/packages/{id}/review-proof ────────────────────────────────────
// decision=accept|reject, reason. Photo-proof fallback in the driver app routes here.

public record ReviewPackageProofCommand(string PackageId, string Decision, string? Reason,
    string StaffId, string StaffName) : IRequest<GrowthCommandResult>;

public class ReviewPackageProofHandler(IApplicationDbContext db, IAuditService audit,
    IRealtimeService realtime) : IRequestHandler<ReviewPackageProofCommand, GrowthCommandResult>
{
    public async Task<GrowthCommandResult> Handle(ReviewPackageProofCommand cmd, CancellationToken ct)
    {
        var pkg = await db.Packages.FirstOrDefaultAsync(p => p.Id == cmd.PackageId, ct);
        if (pkg is null) return new(false, "PACKAGE_NOT_FOUND");
        if (pkg.ProofPhotoUrl is null) return new(false, "NO_PROOF_SUBMITTED");

        var before = new { pkg.Status };

        if (cmd.Decision == "accept")
        {
            pkg.Status      = PackageStatus.Delivered;
            pkg.DeliveredAt = DateTime.UtcNow;
        }
        else
        {
            // Reject: driver needs to reattempt
            pkg.ProofPhotoUrl = null;
            pkg.ProofCode     = null;
        }

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.DocumentAccess,
            "Package", cmd.PackageId,
            reason: $"Proof {cmd.Decision}ed: {cmd.Reason}",
            before: before, after: new { pkg.Status }, ct: ct);
        await db.SaveChangesAsync(ct);

        if (pkg.CourierId is not null)
            await realtime.PublishToDriverAsync(pkg.CourierId, "job.updated",
                new { job_id = pkg.Id, proof_decision = cmd.Decision }, ct);

        return new(true, null);
    }
}

// ── POST /admin/packages/{id}/mark-lost ──────────────────────────────────────
// reason, compensation_amount. Opens a claim.

public record MarkPackageLostCommand(string PackageId, string Reason, long? CompensationAmount,
    string StaffId, string StaffName) : IRequest<GrowthCommandResult>;

public class MarkPackageLostHandler(IApplicationDbContext db, IAuditService audit)
    : IRequestHandler<MarkPackageLostCommand, GrowthCommandResult>
{
    public async Task<GrowthCommandResult> Handle(MarkPackageLostCommand cmd, CancellationToken ct)
    {
        var pkg = await db.Packages.FirstOrDefaultAsync(p => p.Id == cmd.PackageId, ct);
        if (pkg is null) return new(false, "PACKAGE_NOT_FOUND");

        var before = new { pkg.Status };
        pkg.Status = PackageStatus.Returned; // closest available status to "lost"

        // Open a claim (support ticket)
        var ticket = new Domain.Entities.SupportTicket
        {
            UserId      = pkg.SenderId,
            UserRole    = "rider",
            Category    = "lost_package",
            Description = $"Package {pkg.TrackingId} declared lost. Reason: {cmd.Reason}",
            TripId      = null,
            Status      = TicketStatus.Open,
            Priority    = TicketPriority.High,
            Reference   = $"LOST-{pkg.TrackingId}",
            Market      = pkg.Market
        };
        db.SupportTickets.Add(ticket);

        // Compensate sender if amount specified
        if (cmd.CompensationAmount.HasValue && cmd.CompensationAmount.Value > 0)
        {
            var wallet = await db.Wallets.FirstOrDefaultAsync(w => w.UserId == pkg.SenderId, ct);
            if (wallet is not null)
            {
                wallet.Balance += cmd.CompensationAmount.Value;
                db.WalletTransactions.Add(new Domain.Entities.WalletTransaction
                {
                    WalletId    = wallet.Id,
                    Type        = WalletTransactionType.Refund,
                    Amount      = cmd.CompensationAmount.Value,
                    BalanceAfter = wallet.Balance,
                    Title       = "Lost package compensation",
                    ReferenceId = pkg.Id,
                    Reason      = cmd.Reason,
                    CreatedBy   = cmd.StaffId
                });
            }
        }

        await audit.RecordAsync(cmd.StaffId, cmd.StaffName, AuditAction.TripAdjust,
            "Package", cmd.PackageId, cmd.Reason,
            before, new { Status = "Lost", CompensationAmount = cmd.CompensationAmount,
                          ClaimTicketId = ticket.Id }, ct: ct);
        await db.SaveChangesAsync(ct);
        return new(true, null, new { claim_ticket_id = ticket.Id });
    }
}
