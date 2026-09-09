using Izigo.Domain.Common;
using Izigo.Domain.Enums;

namespace Izigo.Domain.Entities;

public class Trip : AuditableEntity
{
    public Trip() => Id = EntityId.ForTrip();

    public string Code { get; set; } = string.Empty;
    public Vertical Vertical { get; set; }
    public ServiceClass ServiceClass { get; set; }
    public string RiderId { get; set; } = string.Empty;
    public string? DriverId { get; set; }
    public string? QuoteId { get; set; }
    public string Market { get; set; } = string.Empty;

    public JobState JobState { get; set; } = JobState.Broadcasting;

    public decimal PickupLat { get; set; }
    public decimal PickupLng { get; set; }
    public string PickupLabel { get; set; } = string.Empty;
    public string? PickupPlaceId { get; set; }
    public decimal DropoffLat { get; set; }
    public decimal DropoffLng { get; set; }
    public string DropoffLabel { get; set; } = string.Empty;
    public string? DropoffPlaceId { get; set; }
    public string? EncodedPolyline { get; set; }
    public int? DistanceM { get; set; }
    public int? DurationS { get; set; }

    public long FareGross { get; set; }
    public long FareBase { get; set; }
    public long FareDistance { get; set; }
    public long FareTime { get; set; }
    public long FareWaiting { get; set; }
    public long FareServiceFee { get; set; }
    public long FareDiscount { get; set; }
    public long FareTip { get; set; }
    public string Currency { get; set; } = "XOF";
    public bool FareIsFinal { get; set; }

    public decimal CommissionRate { get; set; }
    public long CommissionAmount { get; set; }
    public long DriverEarnings { get; set; }
    public long CashCollected { get; set; }
    public long CashToRemit { get; set; }
    public long WalletCredit { get; set; }

    public PaymentMethod PaymentMethod { get; set; }
    public string? PaymentId { get; set; }

    public string? StartOtp { get; set; }
    public string? ShareToken { get; set; }
    public DateTime? ShareTokenExpiresAt { get; set; }

    public string? NoteToDriver { get; set; }
    public string? NoteAudioUrl { get; set; }

    public int? RatingByRider { get; set; }
    public int? RatingByDriver { get; set; }

    public DateTime? ScheduledAt { get; set; }

    public string? ForSomeoneElseName { get; set; }
    public string? ForSomeoneElsePhone { get; set; }

    public string? CancellationReasonCode { get; set; }
    public long? CancellationFee { get; set; }

    public DateTime? AssignedAt { get; set; }
    public DateTime? ArrivedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime? CancelledAt { get; set; }

    public ICollection<TripStateHistory> StateHistory { get; set; } = [];
    public ICollection<SosIncident> SosIncidents { get; set; } = [];
    public Conversation? Conversation { get; set; }
}

public class TripStateHistory : BaseEntity
{
    public TripStateHistory() => Id = EntityId.ForTripStateHistory();
    public string TripId { get; set; } = string.Empty;
    public JobState State { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public string Actor { get; set; } = "system";
    public string? ActorId { get; set; }
}

public class Quote : BaseEntity
{
    public Quote() => Id = EntityId.ForQuote();

    public string RiderId { get; set; } = string.Empty;
    public Vertical Vertical { get; set; }
    public decimal PickupLat { get; set; }
    public decimal PickupLng { get; set; }
    public string PickupLabel { get; set; } = string.Empty;
    public decimal DropoffLat { get; set; }
    public decimal DropoffLng { get; set; }
    public string DropoffLabel { get; set; } = string.Empty;
    public int DistanceM { get; set; }
    public int DurationS { get; set; }
    public string? EncodedPolyline { get; set; }
    public string OptionsJson { get; set; } = "[]";
    public bool SurgeActive { get; set; }
    public decimal SurgeMultiplier { get; set; } = 1.0m;
    public string? SurgeReason { get; set; }
    public string? PromoCode { get; set; }
    public string Currency { get; set; } = "XOF";
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }
}

public class CoRideListing : AuditableEntity
{
    public CoRideListing() => Id = EntityId.ForCoRideListing();

    public string DriverId { get; set; } = string.Empty;
    public decimal FromLat { get; set; }
    public decimal FromLng { get; set; }
    public string FromLabel { get; set; } = string.Empty;
    public decimal ToLat { get; set; }
    public decimal ToLng { get; set; }
    public string ToLabel { get; set; } = string.Empty;
    public DateTime DepartureAt { get; set; }
    public int SeatsTotal { get; set; }
    public int SeatsTaken { get; set; }
    public long PricePerSeat { get; set; }
    public long ServiceFee { get; set; }
    public string Currency { get; set; } = "XOF";
    public bool IsEco { get; set; }
    public bool IsRecurring { get; set; }
    public string Status { get; set; } = "open";
    public string? CancellationReason { get; set; }

    public ICollection<CoRideBooking> Bookings { get; set; } = [];
    public int SeatsLeft => SeatsTotal - SeatsTaken;
}

public class CoRideBooking : AuditableEntity
{
    public CoRideBooking() => Id = EntityId.ForCoRideBooking();

    public string ListingId { get; set; } = string.Empty;
    public CoRideListing Listing { get; set; } = null!;
    public string RiderId { get; set; } = string.Empty;
    public int Seats { get; set; }
    public string SeatLabelsJson { get; set; } = "[]";
    public long PricePerSeat { get; set; }
    public long ServiceFee { get; set; }
    public long PromoDiscount { get; set; }
    public long Total { get; set; }
    public string Currency { get; set; } = "XOF";
    public CoRideBookingStatus Status { get; set; } = CoRideBookingStatus.Upcoming;
    public PaymentMethod PaymentMethod { get; set; }
    public string? PaymentId { get; set; }
    public string? CancellationReason { get; set; }
    public int? RatingByRider { get; set; }
}

public class Package : AuditableEntity
{
    public Package() => Id = EntityId.ForPackage();

    public string TrackingId { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string? CourierId { get; set; }
    public string QuoteId { get; set; } = string.Empty;

    public decimal PickupLat { get; set; }
    public decimal PickupLng { get; set; }
    public string PickupLabel { get; set; } = string.Empty;
    public decimal DropoffLat { get; set; }
    public decimal DropoffLng { get; set; }
    public string DropoffLabel { get; set; } = string.Empty;

    public string RecipientName { get; set; } = string.Empty;
    public string RecipientPhone { get; set; } = string.Empty;
    public string Size { get; set; } = "small";
    public bool IsExpress { get; set; }
    public bool IsFragile { get; set; }
    public string? Description { get; set; }
    public string? Instructions { get; set; }
    public bool RecipientPays { get; set; }
    public long? DeclaredValue { get; set; }

    public PackageStatus Status { get; set; } = PackageStatus.Searching;
    public string? ProofCode { get; set; }
    public string? ProofPhotoUrl { get; set; }
    public string? FailedDeliveryReason { get; set; }

    public long FareTotal { get; set; }
    public string Currency { get; set; } = "XOF";
    public PaymentMethod PaymentMethod { get; set; }
    public string? PaymentId { get; set; }
    public string Market { get; set; } = string.Empty;

    public DateTime? PickedUpAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public DateTime? CancelledAt { get; set; }
}
