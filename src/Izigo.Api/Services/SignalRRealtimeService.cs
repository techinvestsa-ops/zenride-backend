using Izigo.Api.Hubs;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Realtime.Dtos;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Izigo.Api.Services;

/// <summary>
/// Publishes realtime events to connected Flutter / Next.js clients via SignalR.
///
/// Group routing:
///   PublishToUserAsync   → rider-{userId}
///   PublishToDriverAsync → driver-{driverId}
///   PublishToTripAsync   → trip-{tripId}
///   PublishToAdminAsync  → admin-{market}
///
/// Group membership is managed server-side in IzigoHub / AdminHub on connect.
/// Clients do not subscribe to groups — the server assigns them based on JWT claims.
/// </summary>
public sealed class SignalRRealtimeService(
    IHubContext<IzigoHub> appHub,
    IHubContext<AdminHub> adminHub,
    ILogger<SignalRRealtimeService> logger) : IRealtimeService
{
    public RealtimeConfigDto GetConfig() => new("signalr", "/hubs/izigo", true);

    // SignalR handles auth natively via JWT at the hub level.
    // This method is kept to satisfy the IRealtimeService interface.
    public Task<RealtimeAuthDto> AuthorizeChannelAsync(
        string userId, string role, string socketId, string channelName,
        CancellationToken ct = default)
        => Task.FromResult(new RealtimeAuthDto(string.Empty, null));

    public Task PublishToUserAsync(string userId, string eventName, object payload,
        CancellationToken ct = default)
    {
        logger.LogDebug("[Realtime] {Event} → rider-{UserId}", eventName, userId);
        return appHub.Clients.Group($"rider-{userId}").SendAsync(eventName, payload, ct);
    }

    public Task PublishToDriverAsync(string driverId, string eventName, object payload,
        CancellationToken ct = default)
    {
        logger.LogDebug("[Realtime] {Event} → driver-{DriverId}", eventName, driverId);
        return appHub.Clients.Group($"driver-{driverId}").SendAsync(eventName, payload, ct);
    }

    public Task PublishToTripAsync(string tripId, string eventName, object payload,
        CancellationToken ct = default)
    {
        logger.LogDebug("[Realtime] {Event} → trip-{TripId}", eventName, tripId);
        return appHub.Clients.Group($"trip-{tripId}").SendAsync(eventName, payload, ct);
    }

    public Task PublishToAdminAsync(string market, string eventName, object payload,
        CancellationToken ct = default)
    {
        logger.LogDebug("[Realtime] {Event} → admin-{Market}", eventName, market);
        return adminHub.Clients.Group($"admin-{market}").SendAsync(eventName, payload, ct);
    }
}
