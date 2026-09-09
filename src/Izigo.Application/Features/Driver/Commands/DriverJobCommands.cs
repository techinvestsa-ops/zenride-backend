using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Driver.Dtos;
using Izigo.Application.Features.Driver.Queries;
using Izigo.Application.Features.Realtime.Dtos;
using Izigo.Application.Features.Rides.Helpers;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Driver.Commands;

// ── POST /driver/jobs/{id}/accept ─────────────────────────────────────────────

public record AcceptJobCommand(string DriverId, string TripId) : IRequest<JobDetailDto>;

public class AcceptJobHandler(IApplicationDbContext db, IRealtimeService realtime, IPushService push)
    : IRequestHandler<AcceptJobCommand, JobDetailDto>
{
    public async Task<JobDetailDto> Handle(AcceptJobCommand cmd, CancellationToken ct)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == cmd.TripId && t.DriverId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Job offer not found.");

        if (trip.JobState != JobState.Offered)
            throw new InvalidOperationException(
                $"CONFLICT: Job is in state '{trip.JobState}' — can only accept an Offered job.");

        trip.JobState   = JobState.Accepted;
        trip.AssignedAt = DateTime.UtcNow;

        db.TripStateHistories.Add(new Domain.Entities.TripStateHistory
        {
            TripId     = trip.Id,
            State      = JobState.Accepted,
            OccurredAt = DateTime.UtcNow,
            Actor      = "driver",
            ActorId    = cmd.DriverId,
        });

        await db.SaveChangesAsync(ct);

        if (trip.RiderId != null)
        {
            var driver       = await db.Users.FindAsync([cmd.DriverId], ct);
            var activeVehicle = await db.Vehicles
                                   .FirstOrDefaultAsync(v => v.DriverId == cmd.DriverId && v.IsActive, ct);

            await realtime.PublishToUserAsync(trip.RiderId, "ride.status_changed",
                new RideStatusChangedEvent(
                    RideId:     trip.Id,
                    Status:     "driver_assigned",
                    Driver:     driver == null ? null : new RideStatusDriverInfo(
                        Name:        driver.FullName,
                        Plate:       activeVehicle?.Plate,
                        PhoneMasked: JobNotify.MaskPhone(driver.Phone)),
                    Eta:        null,
                    StartOtp:   null,
                    ChangedAt:  DateTime.UtcNow), ct);

            var driverName = driver?.FirstName ?? "Your driver";
            await JobNotify.PushRiderAsync(db, push, trip.RiderId,
                title: "Driver assigned",
                body:  $"{driverName} is on the way",
                tripId: trip.Id, ct);
        }

        return await JobDetailMapper.BuildAsync(trip, db, ct);
    }
}

// ── POST /driver/jobs/{id}/decline ────────────────────────────────────────────

public record DeclineJobCommand(string DriverId, string TripId, string? Reason) : IRequest;

public class DeclineJobHandler(IApplicationDbContext db, IJobDispatcher jobDispatcher)
    : IRequestHandler<DeclineJobCommand>
{
    public async Task Handle(DeclineJobCommand cmd, CancellationToken ct)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == cmd.TripId && t.DriverId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Job offer not found.");

        if (trip.JobState != JobState.Offered)
            throw new InvalidOperationException("CONFLICT: Job is no longer in an offered state.");

        // Reset to Broadcasting so dispatch can re-offer to the next driver.
        // The Offered history entry with ActorId = this driver is the decline record.
        trip.JobState = JobState.Broadcasting;
        trip.DriverId = null;

        db.TripStateHistories.Add(new Domain.Entities.TripStateHistory
        {
            TripId     = trip.Id,
            State      = JobState.Broadcasting,
            OccurredAt = DateTime.UtcNow,
            Actor      = "system"
        });

        var dp = await db.DriverProfiles.FirstOrDefaultAsync(d => d.UserId == cmd.DriverId, ct);
        if (dp != null)
            dp.AcceptanceRate = Math.Max(0, dp.AcceptanceRate - 0.01m);

        var dispatchJob = new Domain.Entities.BackgroundJob
            { Type = $"dispatch:{trip.Id}", Status = "queued" };
        db.BackgroundJobs.Add(dispatchJob);

        await db.SaveChangesAsync(ct);

        jobDispatcher.Enqueue(dispatchJob.Id, dispatchJob.Type);
    }
}

// ── POST /driver/jobs/{id}/arrived-pickup ─────────────────────────────────────

public record ArrivedPickupCommand(string DriverId, string TripId) : IRequest<JobDetailDto>;

public class ArrivedPickupHandler(IApplicationDbContext db, IRealtimeService realtime, IPushService push)
    : IRequestHandler<ArrivedPickupCommand, JobDetailDto>
{
    public async Task<JobDetailDto> Handle(ArrivedPickupCommand cmd, CancellationToken ct)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == cmd.TripId && t.DriverId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Job not found.");

        if (trip.JobState is not (JobState.Accepted or JobState.EnRouteToPickup))
            throw new InvalidOperationException(
                $"CONFLICT: Cannot mark arrived — job is in state '{trip.JobState}'.");

        trip.JobState  = JobState.ArrivedAtPickup;
        trip.ArrivedAt = DateTime.UtcNow;

        db.TripStateHistories.Add(new Domain.Entities.TripStateHistory
        {
            TripId = trip.Id, State = JobState.ArrivedAtPickup,
            OccurredAt = DateTime.UtcNow, Actor = "driver", ActorId = cmd.DriverId,
        });

        await db.SaveChangesAsync(ct);

        if (trip.RiderId != null)
        {
            var driver        = await db.Users.FindAsync([cmd.DriverId], ct);
            var activeVehicle = await db.Vehicles
                                    .FirstOrDefaultAsync(v => v.DriverId == cmd.DriverId && v.IsActive, ct);

            await realtime.PublishToUserAsync(trip.RiderId, "ride.status_changed",
                new RideStatusChangedEvent(
                    RideId:    trip.Id,
                    Status:    "driver_arrived",
                    Driver:    driver == null ? null : new RideStatusDriverInfo(
                        Name:        driver.FullName,
                        Plate:       activeVehicle?.Plate,
                        PhoneMasked: null),
                    Eta:       null,
                    StartOtp:  trip.StartOtp,
                    ChangedAt: DateTime.UtcNow), ct);

            await JobNotify.PushRiderAsync(db, push, trip.RiderId,
                title: "Driver has arrived",
                body:  "Your driver is waiting — show your 4-digit code",
                tripId: trip.Id, ct);
        }

        return await JobDetailMapper.BuildAsync(trip, db, ct);
    }
}

// ── POST /driver/jobs/{id}/start — verifies 4-digit OTP shown by rider ────────

public record StartJobCommand(string DriverId, string TripId, string Otp) : IRequest<JobDetailDto>;

public class StartJobHandler(IApplicationDbContext db, IRealtimeService realtime, IPushService push)
    : IRequestHandler<StartJobCommand, JobDetailDto>
{
    public async Task<JobDetailDto> Handle(StartJobCommand cmd, CancellationToken ct)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == cmd.TripId && t.DriverId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Job not found.");

        if (trip.JobState != JobState.ArrivedAtPickup)
            throw new InvalidOperationException(
                $"CONFLICT: Cannot start — job is in state '{trip.JobState}'.");

        if (trip.StartOtp != null &&
            !string.Equals(trip.StartOtp, cmd.Otp, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("FORBIDDEN: Invalid start OTP.");

        trip.JobState  = JobState.PickedUp;
        trip.StartedAt = DateTime.UtcNow;

        db.TripStateHistories.Add(new Domain.Entities.TripStateHistory
        {
            TripId = trip.Id, State = JobState.PickedUp,
            OccurredAt = DateTime.UtcNow, Actor = "driver", ActorId = cmd.DriverId,
        });

        await db.SaveChangesAsync(ct);

        if (trip.RiderId != null)
        {
            await realtime.PublishToUserAsync(trip.RiderId, "ride.status_changed",
                new RideStatusChangedEvent(trip.Id, "in_progress", null, null, null, DateTime.UtcNow), ct);

            await JobNotify.PushRiderAsync(db, push, trip.RiderId,
                title: "Trip started",
                body:  $"Enjoy your ride to {trip.DropoffLabel}",
                tripId: trip.Id, ct);
        }

        return await JobDetailMapper.BuildAsync(trip, db, ct);
    }
}

// ── POST /driver/jobs/{id}/arrived-dropoff ────────────────────────────────────

public record ArrivedDropoffCommand(string DriverId, string TripId) : IRequest<JobDetailDto>;

public class ArrivedDropoffHandler(IApplicationDbContext db)
    : IRequestHandler<ArrivedDropoffCommand, JobDetailDto>
{
    public async Task<JobDetailDto> Handle(ArrivedDropoffCommand cmd, CancellationToken ct)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == cmd.TripId && t.DriverId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Job not found.");

        if (trip.JobState is not (JobState.PickedUp or JobState.EnRouteToDropoff))
            throw new InvalidOperationException(
                $"CONFLICT: Cannot mark arrived at dropoff — job is in state '{trip.JobState}'.");

        trip.JobState = JobState.ArrivedAtDropoff;

        db.TripStateHistories.Add(new Domain.Entities.TripStateHistory
        {
            TripId = trip.Id, State = JobState.ArrivedAtDropoff,
            OccurredAt = DateTime.UtcNow, Actor = "driver", ActorId = cmd.DriverId,
        });

        await db.SaveChangesAsync(ct);
        return await JobDetailMapper.BuildAsync(trip, db, ct);
    }
}

// ── POST /driver/jobs/{id}/complete — triggers billing ───────────────────────

public record CompleteJobCommand(string DriverId, string TripId) : IRequest<JobDetailDto>;

public class CompleteJobHandler(
    IApplicationDbContext db,
    IRealtimeService realtime,
    IPushService push) : IRequestHandler<CompleteJobCommand, JobDetailDto>
{
    public async Task<JobDetailDto> Handle(CompleteJobCommand cmd, CancellationToken ct)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == cmd.TripId && t.DriverId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Job not found.");

        if (trip.JobState != JobState.ArrivedAtDropoff)
            throw new InvalidOperationException(
                $"CONFLICT: Cannot complete — job is in state '{trip.JobState}'.");

        var commission = await db.CommissionConfigs
            .OrderByDescending(c => c.EffectiveFrom)
            .FirstOrDefaultAsync(ct);

        var commissionRate  = commission?.CommissionRate ?? 0.25m;
        var gross           = trip.FareGross + trip.FareServiceFee - trip.FareDiscount + trip.FareTip;
        var commissionAmt   = (long)(gross * commissionRate);
        var driverEarnings  = gross - commissionAmt;

        trip.JobState         = JobState.Completed;
        trip.CompletedAt      = DateTime.UtcNow;
        trip.FareIsFinal      = true;
        trip.CommissionRate   = commissionRate;
        trip.CommissionAmount = commissionAmt;
        trip.DriverEarnings   = driverEarnings;

        if (trip.PaymentMethod == PaymentMethod.Cash)
            trip.CashCollected = gross;

        // Debit rider wallet when payment method is Wallet
        if (trip.PaymentMethod == PaymentMethod.Wallet && trip.RiderId != null)
        {
            var riderWallet = await db.Wallets
                .FirstOrDefaultAsync(w => w.UserId == trip.RiderId, ct);
            if (riderWallet != null)
            {
                riderWallet.Balance -= gross;
                db.WalletTransactions.Add(new Domain.Entities.WalletTransaction
                {
                    WalletId     = riderWallet.Id,
                    Type         = Domain.Enums.WalletTransactionType.Trip,
                    Amount       = gross,
                    BalanceAfter = riderWallet.Balance,
                    Title        = $"Trip to {trip.DropoffLabel}",
                    Subtitle     = trip.Code,
                    ReferenceId  = trip.Id,
                    Status       = Domain.Enums.PaymentStatus.Succeeded,
                });
            }
        }

        db.TripStateHistories.Add(new Domain.Entities.TripStateHistory
        {
            TripId = trip.Id, State = JobState.Completed,
            OccurredAt = DateTime.UtcNow, Actor = "driver", ActorId = cmd.DriverId,
        });

        var dp = await db.DriverProfiles
            .Include(d => d.DriverWallet)
            .FirstOrDefaultAsync(d => d.UserId == cmd.DriverId, ct);

        long walletBalanceAfter = 0;
        string? walletTxnId    = null;

        if (dp != null)
        {
            dp.TotalTripsCompleted++;
            dp.CompletionRate = Math.Min(1m, dp.CompletionRate + 0.001m);

            if (dp.DriverWallet != null)
            {
                if (trip.PaymentMethod != PaymentMethod.Cash)
                {
                    dp.DriverWallet.AvailableBalance += driverEarnings;
                    walletBalanceAfter = dp.DriverWallet.AvailableBalance;

                    var txn = new Domain.Entities.DriverWalletTransaction
                    {
                        DriverWalletId = dp.DriverWallet.Id,
                        Type           = "trip_earning",
                        Amount         = driverEarnings,
                        BalanceAfter   = dp.DriverWallet.AvailableBalance,
                        Status         = Domain.Enums.PaymentStatus.Succeeded,
                        ReferenceId    = trip.Id,
                        Reason         = $"Trip {trip.Code} — {trip.Vertical}",
                    };
                    db.DriverWalletTransactions.Add(txn);
                    walletTxnId = txn.Id;
                }
                else
                {
                    dp.DriverWallet.PendingCashSettlement += commissionAmt;
                    trip.CashToRemit = commissionAmt;

                    var txn = new Domain.Entities.DriverWalletTransaction
                    {
                        DriverWalletId = dp.DriverWallet.Id,
                        Type           = "cash_trip",
                        Amount         = gross,
                        BalanceAfter   = dp.DriverWallet.AvailableBalance,
                        Status         = Domain.Enums.PaymentStatus.Succeeded,
                        ReferenceId    = trip.Id,
                        Reason         = $"Cash trip {trip.Code} — commission {commissionAmt} owed",
                    };
                    db.DriverWalletTransactions.Add(txn);
                    walletTxnId = txn.Id;
                    walletBalanceAfter = dp.DriverWallet.AvailableBalance;
                }
            }
        }

        await db.SaveChangesAsync(ct);

        if (trip.RiderId != null)
        {
            await realtime.PublishToUserAsync(trip.RiderId, "ride.status_changed",
                new RideStatusChangedEvent(trip.Id, "completed", null, null, null, DateTime.UtcNow), ct);

            await JobNotify.PushRiderAsync(db, push, trip.RiderId,
                title: "Trip completed",
                body:  "Thank you for riding with Izigo",
                tripId: trip.Id, ct);
        }

        if (walletTxnId != null)
            await realtime.PublishToDriverAsync(cmd.DriverId, "wallet.balance_changed",
                new WalletBalanceChangedEvent(walletBalanceAfter, driverEarnings, walletTxnId), ct);

        return await JobDetailMapper.BuildAsync(trip, db, ct);
    }
}

// ── POST /driver/jobs/{id}/cancel ────────────────────────────────────────────

public record CancelJobCommand(string DriverId, string TripId, string? Reason) : IRequest;

public class CancelJobHandler(IApplicationDbContext db, IRealtimeService realtime, IPushService push)
    : IRequestHandler<CancelJobCommand>
{
    public async Task Handle(CancelJobCommand cmd, CancellationToken ct)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == cmd.TripId && t.DriverId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Job not found.");

        if (RideProjector.IsTerminal(trip.JobState))
            throw new InvalidOperationException("CONFLICT: Job is already in a terminal state.");

        trip.JobState               = JobState.CancelledByDriver;
        trip.CancellationReasonCode = cmd.Reason;
        trip.CancelledAt            = DateTime.UtcNow;

        var dp = await db.DriverProfiles
            .FirstOrDefaultAsync(d => d.UserId == cmd.DriverId, ct);
        if (dp != null)
            dp.CancellationRate = Math.Min(1m, dp.CancellationRate + 0.01m);

        await db.SaveChangesAsync(ct);

        if (trip.RiderId != null)
        {
            await realtime.PublishToUserAsync(trip.RiderId, "ride.status_changed",
                new RideStatusChangedEvent(trip.Id, "cancelled", null, null, null, DateTime.UtcNow), ct);

            await JobNotify.PushRiderAsync(db, push, trip.RiderId,
                title: "Driver cancelled",
                body:  "We'll find you another driver",
                tripId: trip.Id, ct);
        }
    }
}

// ── POST /driver/jobs/{id}/rate-customer ─────────────────────────────────────

public record RateCustomerCommand(
    string DriverId, string TripId, int Stars, string? Comment) : IRequest;

public class RateCustomerHandler(IApplicationDbContext db)
    : IRequestHandler<RateCustomerCommand>
{
    public async Task Handle(RateCustomerCommand cmd, CancellationToken ct)
    {
        if (cmd.Stars is < 1 or > 5)
            throw new ArgumentException("VALIDATION_ERROR: Stars must be 1–5.");

        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == cmd.TripId && t.DriverId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Job not found.");

        if (trip.JobState != JobState.Completed)
            throw new InvalidOperationException("CONFLICT: Can only rate after job is completed.");

        if (trip.RatingByDriver.HasValue)
            throw new InvalidOperationException("CONFLICT: Customer already rated for this trip.");

        trip.RatingByDriver = cmd.Stars;

        var rider = await db.Users.FirstOrDefaultAsync(u => u.Id == trip.RiderId, ct);
        if (rider != null)
        {
            var total = rider.TotalRatings;
            rider.Rating = ((rider.Rating * total) + cmd.Stars) / (total + 1);
            rider.TotalRatings++;
        }

        await db.SaveChangesAsync(ct);
    }
}

// ── POST /driver/jobs/{id}/waiting ───────────────────────────────────────────
// Calculates accrued waiting fee from arrival time using FareRule.WaitingPerMin.

public record StartWaitingCommand(string DriverId, string TripId)
    : IRequest<Dtos.WaitingFeeDto>;

public class StartWaitingHandler(IApplicationDbContext db)
    : IRequestHandler<StartWaitingCommand, Dtos.WaitingFeeDto>
{
    public async Task<Dtos.WaitingFeeDto> Handle(StartWaitingCommand cmd, CancellationToken ct)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == cmd.TripId && t.DriverId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Job not found.");

        if (trip.JobState != JobState.ArrivedAtPickup)
            throw new InvalidOperationException(
                "CONFLICT: Waiting timer only applies when at pickup.");

        // Waiting starts from when the driver marked arrival
        var arrivedAt      = trip.ArrivedAt ?? DateTime.UtcNow;
        var waitingSeconds = (int)(DateTime.UtcNow - arrivedAt).TotalSeconds;
        var waitingMinutes = waitingSeconds / 60m;

        // Look up the waiting rate for this service class
        var fareRule = await db.FareRules
            .Where(r => r.ServiceClass == trip.ServiceClass && r.IsActive &&
                        r.Market == trip.Market)
            .OrderByDescending(r => r.Version)
            .FirstOrDefaultAsync(ct);

        var waitingPerMin = fareRule?.WaitingPerMin ?? 50L;
        var accruedFee    = (long)(waitingMinutes * waitingPerMin);

        trip.FareWaiting = accruedFee;

        db.TripStateHistories.Add(new Domain.Entities.TripStateHistory
        {
            TripId     = trip.Id,
            State      = trip.JobState,
            OccurredAt = DateTime.UtcNow,
            Actor      = "driver",
            ActorId    = cmd.DriverId
        });

        await db.SaveChangesAsync(ct);

        return new Dtos.WaitingFeeDto(accruedFee, trip.Currency, waitingSeconds);
    }
}

// ── POST /driver/jobs/{id}/en-route ──────────────────────────────────────────
// Accepted → EnRouteToPickup  (driver starts navigation to pickup)
// PickedUp  → EnRouteToDropoff (driver starts navigation to dropoff)

public record EnRouteCommand(string DriverId, string TripId) : IRequest<JobDetailDto>;

public class EnRouteHandler(IApplicationDbContext db, IRealtimeService realtime, IPushService push)
    : IRequestHandler<EnRouteCommand, JobDetailDto>
{
    public async Task<JobDetailDto> Handle(EnRouteCommand cmd, CancellationToken ct)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == cmd.TripId && t.DriverId == cmd.DriverId, ct)
            ?? throw new KeyNotFoundException("Job not found.");

        if (trip.JobState == JobState.Accepted)
        {
            trip.JobState = JobState.EnRouteToPickup;
            db.TripStateHistories.Add(new Domain.Entities.TripStateHistory
            {
                TripId = trip.Id, State = JobState.EnRouteToPickup,
                OccurredAt = DateTime.UtcNow, Actor = "driver", ActorId = cmd.DriverId,
            });
            await db.SaveChangesAsync(ct);

            // Notify rider: driver is on the way (driver_arriving)
            if (trip.RiderId != null)
            {
                var driver        = await db.Users.FindAsync([cmd.DriverId], ct);
                var activeVehicle = await db.Vehicles
                    .FirstOrDefaultAsync(v => v.DriverId == cmd.DriverId && v.IsActive, ct);

                await realtime.PublishToUserAsync(trip.RiderId, "ride.status_changed",
                    new RideStatusChangedEvent(
                        trip.Id, "driver_arriving",
                        driver == null ? null : new RideStatusDriverInfo(
                            driver.FullName, activeVehicle?.Plate,
                            JobNotify.MaskPhone(driver.Phone)),
                        null, null, DateTime.UtcNow), ct);

                await JobNotify.PushRiderAsync(db, push, trip.RiderId,
                    title: "Driver is on the way",
                    body:  "Your driver is heading to your pickup point",
                    tripId: trip.Id, ct);
            }
        }
        else if (trip.JobState == JobState.PickedUp)
        {
            trip.JobState = JobState.EnRouteToDropoff;
            db.TripStateHistories.Add(new Domain.Entities.TripStateHistory
            {
                TripId = trip.Id, State = JobState.EnRouteToDropoff,
                OccurredAt = DateTime.UtcNow, Actor = "driver", ActorId = cmd.DriverId,
            });
            await db.SaveChangesAsync(ct);
            // Rider is in the car — no notification needed
        }
        else
        {
            throw new InvalidOperationException(
                $"CONFLICT: Cannot mark en-route from state '{trip.JobState}'. " +
                "Expected Accepted or PickedUp.");
        }

        return await JobDetailMapper.BuildAsync(trip, db, ct);
    }
}

// ── File-level helpers ────────────────────────────────────────────────────────

file static class JobNotify
{
    public static async Task PushRiderAsync(
        IApplicationDbContext db, IPushService push,
        string riderId, string title, string body, string tripId,
        CancellationToken ct)
    {
        var token = await db.UserDevices
            .Where(d => d.UserId == riderId && d.FcmToken != null)
            .OrderByDescending(d => d.UpdatedAt)
            .Select(d => d.FcmToken!)
            .FirstOrDefaultAsync(ct);

        if (token != null)
            await push.SendAsync(token, title, body,
                type:     "ride.status_changed",
                entityId: tripId,
                deepLink: $"izigo://ride/{tripId}",
                ct:       ct);
    }

    public static string? MaskPhone(string? phone)
    {
        if (phone == null || phone.Length < 6) return null;
        return phone[..4] + " •• •• " + phone[^2..];
    }
}
