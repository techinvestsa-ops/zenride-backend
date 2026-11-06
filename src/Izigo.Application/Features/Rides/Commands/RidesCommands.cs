using Izigo.Application.Common.Helpers;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Settings;
using Izigo.Application.Features.Quotes.Dtos;
using Izigo.Application.Features.Quotes.Helpers;
using Izigo.Application.Features.Admin.Realtime;
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
    IJobDispatcher jobDispatcher, IRealtimeService realtime)
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

        if (paymentMethod == PaymentMethod.Wallet)
            await RiderWalletPayments.EnsureCanPayAsync(db, cmd.RiderId, option.Fare.Total, ct);

        DateTime? scheduledAt = req.ScheduledAt is { } requested
            ? ScheduledRides.Validate(requested, DateTime.UtcNow)
            : null;

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
            StopsJson      = quote.StopsJson,
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
            ScheduledAt    = scheduledAt,
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

        // Trigger dispatch engine — find and notify nearest available driver.
        // A scheduled ride is held until shortly before its pickup time.
        var dispatchJob = new BackgroundJob { Type = $"dispatch:{trip.Id}", Status = "queued" };
        db.BackgroundJobs.Add(dispatchJob);
        await db.SaveChangesAsync(ct);
        if (scheduledAt is { } at)
            jobDispatcher.Schedule(dispatchJob.Id, dispatchJob.Type, at - ScheduledRides.Lead);
        else
            jobDispatcher.Enqueue(dispatchJob.Id, dispatchJob.Type);
        await AdminRealtimeNotify.JobStateChangedAsync(realtime, trip, ct);

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
        await AdminRealtimeNotify.JobStateChangedAsync(realtime, trip, ct);

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

        // Recalculate route from pickup to new dropoff, through the stops.
        var stops = TripStops.Read(trip.StopsJson);
        var route = await geo.GetRouteAsync(
            new GeoPoint((double)trip.PickupLat, (double)trip.PickupLng),
            new GeoPoint(req.NewDropoff.Lat, req.NewDropoff.Lng),
            stops.Count == 0 ? null : stops.Select(s => new GeoPoint(s.Lat, s.Lng)).ToArray(),
            "driving", ct);

        // Reprice for the new route the same way a quote is priced (same class,
        // surge and promo discount), so a longer trip is not driven at the
        // shorter trip's fare. Straight line x1.3 if routing is unavailable.
        var distanceM = route?.DistanceM ?? (int)(LiveEtaDistance(
            (double)trip.PickupLat, (double)trip.PickupLng, req.NewDropoff.Lat, req.NewDropoff.Lng) * 1.3);
        var freeFlowS = route?.DurationS ?? (int)(distanceM / 8.3);
        var trafficS  = route?.DurationInTrafficS ?? freeFlowS;
        var rule = await db.FareRules
                       .Where(r => r.IsActive && r.ServiceClass == trip.ServiceClass &&
                                   (r.Market == trip.Market || r.Market == ""))
                       .OrderByDescending(r => r.Version)
                       .FirstOrDefaultAsync(ct)
                   ?? FareCalculator.DefaultRule(trip.ServiceClass);
        var surge = await db.Quotes
            .Where(q => q.Id == trip.QuoteId)
            .Select(q => (decimal?)q.SurgeMultiplier)
            .FirstOrDefaultAsync(ct) ?? 1.0m;
        var fare = FareCalculator.Calculate(rule, distanceM, freeFlowS, trafficS, surge, trip.FareDiscount);

        // A wallet ride must still be payable at the new price.
        if (trip.PaymentMethod == PaymentMethod.Wallet)
            await RiderWalletPayments.EnsureCanPayAsync(db, trip.RiderId, fare.Total + trip.FareServiceFee, ct);

        trip.DropoffLat     = (decimal)req.NewDropoff.Lat;
        trip.DropoffLng     = (decimal)req.NewDropoff.Lng;
        trip.DropoffLabel   = req.NewDropoff.Label;
        trip.DropoffPlaceId = req.NewDropoff.PlaceId;
        trip.DistanceM      = distanceM;
        trip.DurationS      = Math.Max(freeFlowS, trafficS);
        if (route != null) trip.EncodedPolyline = route.EncodedPolyline;

        trip.FareGross    = fare.Total - fare.ServiceFee + fare.Discount;
        trip.FareBase     = fare.Base;
        trip.FareDistance = fare.Distance;
        trip.FareTime     = fare.Time;
        trip.FareDiscount = fare.Discount;

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
                },
                fare_total = trip.FareGross + trip.FareServiceFee - trip.FareDiscount,
                currency   = trip.Currency,
            }, ct);
        }

        return await RideDetailMapper.BuildAsync(trip, db, ct, appOptions.Value.ShareBaseUrl, quoteOptions.Value.DefaultCancellationFee);
    }

    private static double LiveEtaDistance(double lat1, double lng1, double lat2, double lng2)
    {
        const double r = 6_371_000;
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLng = (lng2 - lng1) * Math.PI / 180;
        var h = Math.Pow(Math.Sin(dLat / 2), 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Pow(Math.Sin(dLng / 2), 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(h)));
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
        if (req.TipAmount is > 0)
            await RideTips.ApplyAsync(db, trip, req.TipAmount.Value, ct);

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

        await RideTips.ApplyAsync(db, trip, req.Amount, ct);

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
