using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace Izigo.Api.Hubs;

/// <summary>
/// SignalR's default provider only reads <see cref="ClaimTypes.NameIdentifier"/>.
/// App JWTs keep inbound claims unmapped, so the stable id is <c>sub</c>.
/// </summary>
public sealed class IzigoUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
        => connection.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
           ?? connection.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
           ?? connection.User?.FindFirst("sub")?.Value;
}
