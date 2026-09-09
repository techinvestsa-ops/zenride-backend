using Izigo.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1;

/// <summary>
/// Module 24 — Realtime Channels (SignalR).
///
/// GET  /realtime/config  → returns hub URL and transport ("signalr") so Flutter
///                          clients do not hardcode connection details.
/// POST /realtime/auth    → kept for backward compatibility; returns the hub URL
///                          since SignalR auth is handled at the hub via bearer JWT.
///
/// Flutter connection pattern:
///   final hub = HubConnectionBuilder()
///     .withUrl("${baseUrl}/hubs/izigo?access_token=$jwt")
///     .build();
/// </summary>
[Route("api/v1/realtime")]
[Authorize(Policy = "AppPolicy")]
public class RealtimeController(IRealtimeService realtime) : BaseController
{
    /// <summary>Returns the SignalR hub URL and transport for client initialisation.</summary>
    [HttpGet("config")]
    public IActionResult GetConfig()
        => Ok(new { success = true, data = realtime.GetConfig() });

    /// <summary>
    /// Kept for backward compatibility. With SignalR, no explicit channel auth step is
    /// needed — the client connects to /hubs/izigo directly using its bearer token.
    /// Returns the hub URL the client should connect to.
    /// </summary>
    [HttpPost("auth")]
    public IActionResult Auth()
        => Ok(new { success = true, data = new { hub_url = "/hubs/izigo" } });
}
