namespace Izigo.Application.Features.Realtime.Dtos;

// ── GET /realtime/config ──────────────────────────────────────────────────────
// Returned to the Flutter client so it can initialise the SignalR SDK.
// The client connects to HubUrl using its bearer token as a query param:
//   wss://api.izigo.app/hubs/izigo?access_token=<jwt>

public record RealtimeConfigDto(
    string Transport,  // "signalr"
    string HubUrl,     // "/hubs/izigo" (app) | "/hubs/admin" (admin console)
    bool   Tls         // true in production
);

// ── Kept for backward compat ──────────────────────────────────────────────────
// SignalR auth is handled at the hub via bearer token — no explicit auth step needed.

public record RealtimeAuthRequest(string SocketId, string ChannelName);

public record RealtimeAuthDto(string Auth, string? ChannelData);

// ─────────────────────────────────────────────────────────────────────────────
// Event payload records — one per spec event, keyed by event name.
// All are serialised to snake_case JSON before being published.
// ─────────────────────────────────────────────────────────────────────────────

// ride.status_changed  → private-rider.{user_id}
public record RideStatusChangedEvent(
    string RideId,
    string Status,          // driver_assigned | driver_arriving | driver_arrived | in_progress | completed | cancelled | no_drivers_found
    RideStatusDriverInfo? Driver,
    RideStatusEta? Eta,
    string? StartOtp,       // present only on driver_arrived
    DateTime ChangedAt
);
public record RideStatusDriverInfo(string Name, string? Plate, string? PhoneMasked);
public record RideStatusEta(int PickupMin, int DropoffMin);

// driver.location  → presence-trip.{trip_id}
public record DriverLocationEvent(
    string TripId,
    double Lat,
    double Lng,
    double? Heading,
    int? EtaMin
);

// ride.no_drivers_found  → private-rider.{user_id}
public record NoDriversFoundEvent(string RideId, bool RetryAllowed);

// package.status_changed  → private-rider.{user_id}
public record PackageStatusChangedEvent(
    string TrackingId,
    string Status,
    PackageCourierInfo? Courier,
    int Progress,
    int? EtaMin
);
public record PackageCourierInfo(string FirstName, string? VehiclePlate);

// coride.request_matched  → private-rider.{user_id}
public record CoRideRequestMatchedEvent(string RequestId, object Listing);

// coride.booking_updated  → private-rider.{user_id}
public record CoRideBookingUpdatedEvent(string BookingId, string Status, DateTime? DepartureAt);

// job.offered  → private-driver.{driver_id}  (+ high-priority FCM)
public record JobOfferedEvent(
    string TripId,
    string Vertical,
    string State,
    int ExpiresInS,
    DateTime OfferedAt,
    JobLocationInfo Pickup,
    JobLocationInfo Dropoff,
    JobCustomerInfo Customer,
    double DistanceKm,
    int EtaMin,
    long EstimatedEarnings,
    string Currency,
    string PaymentMethod,
    string? SubLabel,
    string? EncodedPolyline
);
public record JobLocationInfo(string Label, double Lat, double Lng);
public record JobCustomerInfo(string FirstName, double Rating);

// job.offer_revoked  → private-driver.{driver_id}
public record JobOfferRevokedEvent(string JobId, string Reason);  // expired | taken | cancelled

// job.updated  → private-driver.{driver_id}
public record JobUpdatedEvent(string TripId, string State, string? Reason);

// kyc.status_changed  → private-driver.{driver_id}
public record KycStatusChangedEvent(string Status, string[] RejectedSteps);

// wallet.balance_changed  → private-rider or private-driver channel
public record WalletBalanceChangedEvent(long Balance, long Delta, string TransactionId);

// chat.message  → private-rider / private-driver (recipient's channel)
public record ChatMessageEvent(string ConversationId, ChatMessageInfo Message);
public record ChatMessageInfo(
    string Id, string SenderId, string SenderRole,
    string Type, string? Body, string? MediaUrl, int? DurationS,
    string? LocalId, bool IsRead, DateTime SentAt
);

// notification.created  → private-rider or private-driver
public record NotificationCreatedEvent(NotificationInfo Notification, int UnreadCount);
public record NotificationInfo(
    string Id, string Type, string Title, string Body,
    string? ImageUrl, string? DeepLink, string Group, DateTime CreatedAt
);

// broadcast.message  → admin channel (all users via server-side fan-out)
public record BroadcastMessageEvent(string Title, string Body, string Severity, string Audience);
