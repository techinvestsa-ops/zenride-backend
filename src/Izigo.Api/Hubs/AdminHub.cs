using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Izigo.Api.Hubs;

/// <summary>
/// Admin-facing SignalR hub for the Next.js operator console.
///
/// Group layout:
///   admin-ci  — events for the Ivory Coast market
///   admin-ng  — events for the Nigerian market
///
/// On connect, the staff member is added to every market group their JWT grants.
/// The JWT carries a "markets" claim (e.g. "ci,ng") set at login time.
///
/// Authentication: AdminBearer JWT — pass via Authorization header or
/// ?access_token=&lt;jwt&gt; query param (required for WebSocket upgrade).
/// </summary>
[Authorize(Policy = "AdminPolicy")]
public class AdminHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var marketsRaw = Context.User?.FindFirst("markets")?.Value ?? "ci";
        var markets    = marketsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries);

        foreach (var market in markets)
            await Groups.AddToGroupAsync(Context.ConnectionId, $"admin-{market.Trim().ToLower()}");

        await base.OnConnectedAsync();
    }
}
