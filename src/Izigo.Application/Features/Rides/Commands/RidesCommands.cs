using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Settings;
using Izigo.Application.Features.Quotes.Dtos;
using Izigo.Application.Features.Quotes.Helpers;
using Izigo.Application.Features.Rides.Dtos;
using Izigo.Application.Features.Rides.Helpers;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Izigo.Application.Features.Rides.Commands;

// ── POST /rides — requires Idempotency-Key ────────────────────────────────────

public record CreateRideCommand(
    string RiderId,
    CreateRideRequest Request,
    string? IdempotencyKey
) : IRequest<RideDetailDto>;

public class CreateRideHandler(IApplicationDbContext db, IIdempotencyService idempotency,
    IOptions<AppSettings> appOptions, IOptions<QuoteSettings> quoteOptions,
    IJobDispatcher jobDispatcher)
    : IRequestHandler<CreateRideCommand, RideDetailDto>
{
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOpts = new()
        { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower };

    public async Task<RideDetailDto> Handle(CreateRideCommand cmd, CancellationToken ct)
    {
        var req = cmd.Request;

        // Idempotency: return cached response for duplicate requests
        if (cmd.IdempotencyKey != null)
        {
            var cached = await idempotency.GetAsync(cmd.IdempotencyKey, cmd.RiderId, ct);
            if (cached != null)
            {
                var cachedDto = System.Text.Json.JsonSerializer.Deserialize<RideDetailDto>(
                    cached.ResponseJson, _jsonOpts);
                if (cachedDto != null) return cachedDto;
            }
        }

        // Validate quote
        if (!Enum.TryParse<ServiceClass>(req.ClassCode.Replace("_", ""), true, out var serviceClass))
            throw new ArgumentException("VALIDATION_ERROR: Invalid class_code.");

        if (!Enum.TryParse<PaymentMethod>(req.PaymentMethod.Replace("_", ""), true, out var paymentMethod))
            throw new ArgumentException("VALIDATION_ERROR: Invalid payment_method.");

        var quote = await db.Quotes
            .FirstOrDefaultAsync(q => q.Id == req.QuoteId && q.RiderId == cmd.RiderId, ct)
            ?? throw new KeyNotFoundException("Quote not found.");

        if (quote.IsUsed)
        {
            // Natural idempotency: quote is single-use, so a trip must already exist
            var existingTrip = await db.Trips.FirstOrDefaultAsync(t => t.QuoteId == req.QuoteId, ct);
            if (existingTrip != null)
                return await RideDetailMapper.BuildAsync(existingTrip, db, ct, appOptions.Value.ShareBaseUrl, quoteOptions.Value.DefaultCancellationFee);
            throw new InvalidOperationException("CONFLICT: Quote has already been used.");
        }

        if (quote.ExpiresAt <= DateTime.UtcNow)
            throw new InvalidOperationException("CONFLICT: Quote has expired. Please request a new one.");

        // Get fare from quote options
        var options = System.Text.Json.JsonSerializer
            .Deserialize<QuoteOptionDto[]>(quote.OptionsJson) ?? [];

        // Reuse the already-mapped class code from quote options
        var classCodeStr = ToClassCode(serviceClass);
        var option = options.FirstOrDefault(o =>
            string.Equals(o.ClassCode, classCodeStr, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"VALIDATION_ERROR: class_code '{req.ClassCode}' not in this quote.");

        // Mark quote used
        quote.IsUsed = true;

        // Generate a 4-digit start OTP
        var startOtp = Random.Shared.Next(1000, 9999).ToString();

        var trip = new Trip
        {
            Code           = TripCode.GenerateRide(),
            Vertical       = quote.Vertical,
            ServiceClass   = serviceClass,
            RiderId        = cmd.RiderId,
            QuoteId        = quote.Id,
            Market         = "ci",   // from config in production

            JobState       = JobState.Broadcasting,

            PickupLat      = quote.PickupLat,
            PickupLng      = quote.PickupLng,
            PickupLabel    = quote.PickupLabel,
            DropoffLat     = quote.DropoffLat,
            DropoffLng     = quote.DropoffLng,
            DropoffLabel   = quote.DropoffLabel,
            EncodedPolyline = quote.EncodedPolyline,
            DistanceM      = quote.DistanceM,
            DurationS      = quote.DurationS,

            // Server owns fares — taken from the validated quote, never from client
            FareGross      = option.Fare.Total - option.Fare.ServiceFee + option.Fare.Discount,
            FareBase       = option.Fare.Base,
            FareDistance   = option.Fare.Distance,
            FareTime       = option.Fare.Time,
            FareServiceFee = option.Fare.ServiceFee,
            FareDiscount   = option.Fare.Discount,
            Currency       = quote.Currency,

            PaymentMethod  = paymentMethod,
            StartOtp       = startOtp,
            ScheduledAt    = req.ScheduledAt,
            NoteToDriver   = req.Note,

            ForSomeoneElseName  = req.ForSomeoneElse?.Name,
            ForSomeoneElsePhone = req.ForSomeoneElse?.Phone
        };

        db.Trips.Add(trip);
        db.TripStateHistories.Add(new TripStateHistory
        {
            TripId      = trip.Id,
            State       = JobState.Broadcasting,
            OccurredAt  = DateTime.UtcNow,
            Actor       = "system"
        });

        await db.SaveChangesAsync(ct);

        // Trigger dispatch engine — find and notify nearest available driver
        var dispatchJob = new BackgroundJob { Type = $"dispatch:{trip.Id}", Status = "queued" };
        db.BackgroundJobs.Add(dispatchJob);
        await db.SaveChangesAsync(ct);
        jobDispatcher.Enqueue(dispatchJob.Id, dispatchJob.Type);

        var result = await RideDetailMapper.BuildAsync(trip, db, ct, appOptions.Value.ShareBaseUrl, quoteOptions.Value.DefaultCancellationFee);

        if (cmd.IdempotencyKey != null)
            await idempotency.SaveAsync(cmd.IdempotencyKey, cmd.RiderId, "POST /rides",
                System.Text.Json.JsonSerializer.Serialize(result, _jsonOpts), 200, ct);

        return result;
    }

    private static string ToClassCode(ServiceClass sc) => sc switch
    {
        ServiceClass.ZenCar      => "zen_car",
        ServiceClass.ZenBike     => "zen_bike",
        ServiceClass.ZenCoRide   => "zen_coride",
        ServiceClass.PackageSmall => "package_small",
        ServiceClass.PackageLarge => "package_large",
        _ => sc.ToString().ToLower()
    };
}

// ── POST /rides/{id}/cancel ───────────────────────────────────────────────────

public record CancelRideCommand(
    string RiderId, string TripId, string ReasonCode, string? Note
) : IRequest<CancelRideResult>;

public class CancelRideHandler(IApplicationDbContext db, IRealtimeService realtime, IPushService push)
    : IRequestHandler<CancelRideCommand, CancelRideResult>
{
    public async Task<CancelRideResult> Handle(CancelRideCommand req, CancellationToken ct)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == req.TripId && t.RiderId == req.RiderId, ct)
            ?? throw new KeyNotFoundException("Ride not found.");

        if (RideProjector.IsTerminal(trip.JobState))
            throw new InvalidOperationException("CONFLICT: Ride is already in a terminal state.");

        // Cancellation fee applies once driver has arrived at pickup
        var charged = trip.JobState == JobState.ArrivedAtPickup;
        long fee = 0;

        if (charged)
        {
            var rule = await db.FareRules
                .Where(r => r.IsActive &&
                            r.ServiceClass == trip.ServiceClass &&
                            r.Market == trip.Market)
                .OrderByDescending(r => r.Version)
                .FirstOrDefaultAsync(ct);

            fee = rule?.CancellationFee ?? 500;   // fallback 500 XOF if no rule seeded
        }

        trip.JobState = JobState.CancelledByRider;
        trip.CancellationReasonCode = req.ReasonCode;
        trip.CancellationFee = fee;
        trip.CancelledAt = DateTime.UtcNow;

        db.TripStateHistories.Add(new TripStateHistory
        {
            TripId = trip.Id, State = JobState.CancelledByRider,
            OccurredAt = DateTime.UtcNow, Actor = "rider", ActorId = req.RiderId
        });

        await db.SaveChangesAsync(ct);

        // Notify driver if one was assigned
        if (trip.DriverId is not null)
        {
            await realtime.PublishToDriverAsync(trip.DriverId, "job.updated", new
            {
                job_id = trip.Id,
                state  = "cancelled_by_rider",
                reason = req.ReasonCode
            }, ct);

            var fcmToken = await db.UserDevices
                .Where(d => d.UserId == trip.DriverId && d.FcmToken != null)
                .OrderByDescending(d => d.UpdatedAt)
                .Select(d => d.FcmToken!)
                .FirstOrDefaultAsync(ct);

            if (fcmToken is not null)
                await push.SendAsync(fcmToken,
                    title:    "Rider cancelled",
                    body:     "The rider has cancelled the trip.",
                    type:     "job.updated",
                    entityId: trip.Id,
                    deepLink: $"izigo://driver/job/{trip.Id}",
                    ct:       ct);
        }

        return new CancelRideResult(fee, charged);
    }
}

// ── PATCH /rides/{id}/destination ────────────────────────────────────────────

public record ChangeDestinationCommand(
    string RiderId, string TripId, ChangeDestinationRequest NewDropoff
) : IRequest<RideDetailDto>;

public class ChangeDestinationHandler(IApplicationDbContext db, IGeoService geo,
    IOptions<AppSettings> appOptions, IOptions<QuoteSettings> quoteOptions,
    IRealtimeService realtime)
    : IRequestHandler<ChangeDestinationCommand, RideDetailDto>
{
    public async Task<RideDetailDto> Handle(ChangeDestinationCommand req, CancellationToken ct)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == req.TripId && t.RiderId == req.RiderId, ct)
            ?? throw new KeyNotFoundException("Ride not found.");

        if (trip.JobState is not (JobState.PickedUp or JobState.EnRouteToDropoff))
            throw new InvalidOperationException(
                "CONFLICT: Destination can only be changed once the trip is in progress.");

        // Recalculate route from pickup to new dropoff
        var route = await geo.GetRouteAsync(
            new GeoPoint((double)trip.PickupLat, (double)trip.PickupLng),
            new GeoPoint(req.NewDropoff.Lat, req.NewDropoff.Lng),
            null, "driving", ct);

        trip.DropoffLat     = (decimal)req.NewDropoff.Lat;
        trip.DropoffLng     = (decimal)req.NewDropoff.Lng;
        trip.DropoffLabel   = req.NewDropoff.Label;
        trip.DropoffPlaceId = req.NewDropoff.PlaceId;

        if (route != null)
        {
            trip.DistanceM      = route.DistanceM;
            trip.DurationS      = route.DurationS;
            trip.EncodedPolyline = route.EncodedPolyline;
        }

        await db.SaveChangesAsync(ct);

        // Notify driver of the new dropoff via the shared trip room
        if (trip.DriverId is not null)
        {
            await realtime.PublishToTripAsync(trip.Id, "job.updated", new
            {
                job_id      = trip.Id,
                new_dropoff = new
                {
                    label    = trip.DropoffLabel,
                    lat      = (double)trip.DropoffLat,
                    lng      = (double)trip.DropoffLng,
                    place_id = trip.DropoffPlaceId
                }
            }, ct);
        }

        return await RideDetailMapper.BuildAsync(trip, db, ct, appOptions.Value.ShareBaseUrl, quoteOptions.Value.DefaultCancellationFee);
    }
}

// ── PATCH /rides/{id}/payment-method ─────────────────────────────────────────

public record ChangePaymentMethodCommand(
    string RiderId, string TripId, string PaymentMethod
) : IRequest;

public class ChangePaymentMethodHandler(IApplicationDbContext db)
    : IRequestHandler<ChangePaymentMethodCommand>
{
    public async Task Handle(ChangePaymentMethodCommand req, CancellationToken ct)
    {
        if (!Enum.TryParse<PaymentMethod>(
                req.PaymentMethod.Replace("_", ""), true, out var method))
            throw new ArgumentException("VALIDATION_ERROR: Invalid payment method.");

        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == req.TripId && t.RiderId == req.RiderId, ct)
            ?? throw new KeyNotFoundException("Ride not found.");

        if (trip.FareIsFinal)
            throw new InvalidOperationException("CONFLICT: Fare has already been captured.");

        trip.PaymentMethod = method;
        await db.SaveChangesAsync(ct);
    }
}

// ── POST /rides/{id}/note ─────────────────────────────────────────────────────

public record AddNoteCommand(string RiderId, string TripId, string? Text, string? AudioUrl)
    : IRequest;

public class AddNoteHandler(IApplicationDbContext db, IRealtimeService realtime)
    : IRequestHandler<AddNoteCommand>
{
    public async Task Handle(AddNoteCommand req, CancellationToken ct)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == req.TripId && t.RiderId == req.RiderId, ct)
            ?? throw new KeyNotFoundException("Ride not found.");

        trip.NoteToDriver = req.Text ?? trip.NoteToDriver;
        trip.NoteAudioUrl = req.AudioUrl ?? trip.NoteAudioUrl;
        await db.SaveChangesAsync(ct);

        // Push note to driver so they can read it before arriving
        if (trip.DriverId is not null)
        {
            await realtime.PublishToDriverAsync(trip.DriverId, "job.updated", new
            {
                job_id         = trip.Id,
                note_to_driver = trip.NoteToDriver,
                note_audio_url = trip.NoteAudioUrl
            }, ct);
        }
    }
}

// ── POST /rides/{id}/rate — 409 on repeat ─────────────────────────────────────

public record RateRideCommand(
    string RiderId, string TripId, int Stars,
    string[]? Tags, string? Comment, long? TipAmount
) : IRequest;

public class RateRideHandler(IApplicationDbContext db) : IRequestHandler<RateRideCommand>
{
    public async Task Handle(RateRideCommand req, CancellationToken ct)
    {
        if (req.Stars is < 1 or > 5)
            throw new ArgumentException("VALIDATION_ERROR: Stars must be between 1 and 5.");

        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == req.TripId && t.RiderId == req.RiderId, ct)
            ?? throw new KeyNotFoundException("Ride not found.");

        if (trip.RatingByRider.HasValue)
            throw new InvalidOperationException("CONFLICT: You have already rated this trip.");

        trip.RatingByRider = req.Stars;
        if (req.TipAmount.HasValue) trip.FareTip = req.TipAmount.Value;

        // Update driver's running rating average
        if (trip.DriverId != null)
        {
            var dp = await db.DriverProfiles
                .Include(d => d.User)
                .FirstOrDefaultAsync(d => d.UserId == trip.DriverId, ct);

            if (dp != null)
            {
                var total = dp.User.TotalRatings;
                var current = dp.User.Rating;
                dp.User.Rating = ((current * total) + req.Stars) / (total + 1);
                dp.User.TotalRatings++;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}

// ── POST /rides/{id}/tip ──────────────────────────────────────────────────────

public record TipRideCommand(string RiderId, string TripId, long Amount) : IRequest;

public class TipRideHandler(IApplicationDbContext db) : IRequestHandler<TipRideCommand>
{
    public async Task Handle(TipRideCommand req, CancellationToken ct)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == req.TripId && t.RiderId == req.RiderId, ct)
            ?? throw new KeyNotFoundException("Ride not found.");

        if (trip.JobState != JobState.Completed)
            throw new InvalidOperationException("CONFLICT: Can only tip on completed trips.");

        // Gate on tipping feature flag
        var config = await db.PlatformConfigs
            .FirstOrDefaultAsync(c => c.Market == trip.Market, ct);
        if (config is not null && !config.TippingEnabled)
            throw new InvalidOperationException("CONFLICT: Tipping is not enabled in this market.");

        trip.FareTip = req.Amount;

        // Credit tip directly to driver wallet
        if (trip.DriverId is not null)
        {
            var wallet = await db.DriverWallets
                .FirstOrDefaultAsync(w => w.DriverId == trip.DriverId, ct);
            if (wallet is not null)
            {
                wallet.AvailableBalance += req.Amount;
                db.DriverWalletTransactions.Add(new Domain.Entities.DriverWalletTransaction
                {
                    DriverWalletId = wallet.Id,
                    Type           = "tip",
                    Amount         = req.Amount,
                    BalanceAfter   = wallet.AvailableBalance,
                    ReferenceId    = trip.Id
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}

// ── POST /rides/{id}/lost-item ────────────────────────────────────────────────

public record ReportLostItemCommand(string RiderId, string TripId, string Description)
    : IRequest;

public class ReportLostItemHandler(IApplicationDbContext db)
    : IRequestHandler<ReportLostItemCommand>
{
    public async Task Handle(ReportLostItemCommand req, CancellationToken ct)
    {
        _ = await db.Trips
            .FirstOrDefaultAsync(t => t.Id == req.TripId && t.RiderId == req.RiderId, ct)
            ?? throw new KeyNotFoundException("Ride not found.");

        db.SupportTickets.Add(new SupportTicket
        {
            UserId      = req.RiderId,
            UserRole    = "rider",
            Category    = "lost_item",
            Description = req.Description,
            TripId      = req.TripId,
            Reference   = $"TKT-{Guid.CreateVersion7():N}"[..12]
        });

        await db.SaveChangesAsync(ct);
    }
}

// ── DELETE /rides/scheduled/{id} ─────────────────────────────────────────────

public record CancelScheduledRideCommand(string RiderId, string TripId) : IRequest;

public class CancelScheduledRideHandler(IApplicationDbContext db)
    : IRequestHandler<CancelScheduledRideCommand>
{
    public async Task Handle(CancelScheduledRideCommand req, CancellationToken ct)
    {
        var trip = await db.Trips
            .FirstOrDefaultAsync(t =>
                t.Id == req.TripId &&
                t.RiderId == req.RiderId &&
                t.ScheduledAt != null &&
                t.JobState == JobState.Broadcasting, ct)
            ?? throw new KeyNotFoundException("Scheduled ride not found.");

        trip.JobState    = JobState.CancelledByRider;
        trip.CancelledAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}

// Alias so the quotes file can use this type
internal record QuoteOptionDto(
    string ClassCode, string Name, string Icon, int Seats,
    int EtaPickupMin, int DurationMin, bool Available,
    FareDetail Fare);

internal record FareDetail(
    long Total, long Base, long Distance,
    long Time, long ServiceFee, long Discount);
