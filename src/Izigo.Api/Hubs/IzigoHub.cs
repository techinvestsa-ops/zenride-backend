using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Izigo.Api.Hubs;

/// <summary>
/// App-facing SignalR hub for riders and drivers.
///
/// Group layout (mirrors the old Pusher channel names):
///   rider-{userId}   — rider-specific events (ride status, wallet, notifications)
///   driver-{userId}  — driver events (job offers, KYC, wallet)
///   trip-{tripId}    — shared trip room (driver location, chat, state changes)
///
/// Authentication: AppBearer JWT — pass via Authorization header or
/// ?access_token=&lt;jwt&gt; query param (required for WebSocket upgrade).
/// </summary>
[Authorize(Policy = "AppPolicy")]
public class IzigoHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        var role   = Context.User?.FindFirst("role")?.Value;

        if (userId is not null)
        {
            var group = role == "driver" ? $"driver-{userId}" : $"rider-{userId}";
            await Groups.AddToGroupAsync(Context.ConnectionId, group);
        }

        await base.OnConnectedAsync();
    }

    /// <summary>Join the shared trip room to receive driver location and chat events.</summary>
    public Task JoinTripRoom(string tripId)
        => Groups.AddToGroupAsync(Context.ConnectionId, $"trip-{tripId}");

    /// <summary>Leave the trip room when the ride ends or the screen is closed.</summary>
    public Task LeaveTripRoom(string tripId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"trip-{tripId}");
}
