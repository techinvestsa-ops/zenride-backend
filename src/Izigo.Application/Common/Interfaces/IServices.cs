using Izigo.Domain.Entities;

namespace Izigo.Application.Common.Interfaces;

public interface ITokenService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
    string? ValidateRefreshToken(string token);

    string GenerateAdminAccessToken(Staff staff);
    string GenerateAdminRefreshToken();
    string? ValidateAdminRefreshToken(string token);
}

public interface IOtpService
{
    Task<string> GenerateAndSendAsync(string identifier, string purpose, CancellationToken ct = default);
}

public interface ISmsService
{
    Task SendAsync(string to, string message, CancellationToken ct = default);
}

public interface IPushService
{
    Task SendAsync(string fcmToken, string title, string body, string type,
        string entityId, string? deepLink = null, string? payloadJson = null,
        bool highPriority = false, CancellationToken ct = default);
    Task SendBatchAsync(IEnumerable<string> fcmTokens, string title, string body,
        string type, string? deepLink = null, CancellationToken ct = default);
}

public interface IRealtimeService
{
    // Publish to channel
    Task PublishToUserAsync(string userId, string eventName, object payload, CancellationToken ct = default);
    Task PublishToDriverAsync(string driverId, string eventName, object payload, CancellationToken ct = default);
    Task PublishToTripAsync(string tripId, string eventName, object payload, CancellationToken ct = default);
    Task PublishToAdminAsync(string market, string eventName, object payload, CancellationToken ct = default);

    // Channel auth (Pusher-style: socket_id + channel_name → signed auth token)
    Task<Features.Realtime.Dtos.RealtimeAuthDto> AuthorizeChannelAsync(
        string userId, string role, string socketId, string channelName,
        CancellationToken ct = default);

    // Config returned to Flutter client via GET /realtime/config
    Features.Realtime.Dtos.RealtimeConfigDto GetConfig();
}

public interface IFileStorageService
{
    Task<string> UploadAsync(Stream stream, string fileName, string contentType, CancellationToken ct = default);
    string GetSignedUrl(string fileKey, int expiryMinutes = 15);
}

public interface IGeoService
{
    Task<GeoAutocompleteResult[]> AutocompleteAsync(string q, double lat, double lng,
        string? sessionToken, string? country, CancellationToken ct = default);
    Task<GeoPlaceDetail?> GetPlaceDetailAsync(string placeId, CancellationToken ct = default);
    Task<GeoReverseResult?> ReverseGeocodeAsync(double lat, double lng, CancellationToken ct = default);
    Task<GeoGeocodeResult?> GeocodeAsync(string address, CancellationToken ct = default);
    Task<GeoRouteResult?> GetRouteAsync(GeoPoint origin, GeoPoint destination,
        GeoPoint[]? waypoints, string mode, CancellationToken ct = default);
    Task<GeoEtaResult[]> GetEtaAsync(GeoPoint origin, GeoPoint[] destinations,
        CancellationToken ct = default);
}

public interface IPaymentGateway
{
    Task<PaymentInitResult> InitiateAsync(
        string paymentId, long amount, string currency,
        string method, string? phone, string? returnUrl,
        CancellationToken ct = default);

    bool VerifyWebhookSignature(string gateway, string rawPayload, string? signature);
}

public interface IAuditService
{
    Task RecordAsync(string staffId, string staffName, Domain.Enums.AuditAction action,
        string? targetType = null, string? targetId = null, string? reason = null,
        object? before = null, object? after = null, string? requestId = null,
        string? ipAddress = null, string market = "ci", CancellationToken ct = default);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string storedHash);
}

public interface IIdempotencyService
{
    Task<IdempotencyRecord?> GetAsync(string key, string userId, CancellationToken ct = default);
    Task SaveAsync(string key, string userId, string endpoint, string responseJson,
        int statusCode, CancellationToken ct = default);
}

public interface ICurrentUserService
{
    string? UserId { get; }
    string? Role { get; }
    string? Market { get; }
    string? XApp { get; }       // rider | driver
    string? IpAddress { get; }  // remote IP for audit and session records
    bool IsAuthenticated { get; }
}

public interface IEmailService
{
    Task SendAsync(string to, string toName, string subject, string htmlBody,
        CancellationToken ct = default);
    /// <summary>Prepends Email:BaseUrl to a relative path, producing an absolute URL for email links.</summary>
    string BuildLink(string path);
}

public interface IJobDispatcher
{
    /// <summary>Enqueues a BackgroundJob record for immediate processing by Hangfire.</summary>
    void Enqueue(string jobId, string jobType);
    /// <summary>Schedules a BackgroundJob record to be processed at a specific UTC time.</summary>
    void Schedule(string jobId, string jobType, DateTime utcAt);
}

public interface ICurrentStaffService
{
    string? StaffId { get; }
    string? Email { get; }
    IReadOnlyList<string> Permissions { get; }
    IReadOnlyList<string> Markets { get; }
    bool HasPermission(string permission);
    bool IsAuthenticated { get; }
}

// DTO types for service contracts
public record GeoAutocompleteResult(string PlaceId, string PrimaryText, string SecondaryText, int? DistanceM);
public record GeoPlaceDetail(string Name, string FormattedAddress, double Lat, double Lng);
public record GeoReverseResult(string FormattedAddress, double Lat, double Lng);
public record GeoGeocodeResult(string FormattedAddress, double Lat, double Lng, string? PlaceId);
public record GeoPoint(double Lat, double Lng);
public record GeoRouteResult(string EncodedPolyline, int DistanceM, int DurationS, int? DurationInTrafficS);
public record GeoEtaResult(double DestLat, double DestLng, int? DurationS, int? DistanceM, string Status);
public record PaymentInitResult(string PaymentId, string Status, string? CheckoutUrl,
    string? UssdCode, string? DeepLink, bool OtpRequired);
