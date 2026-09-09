using System.Security.Claims;
using Izigo.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace Izigo.Infrastructure.Services;

public class CurrentUserService(IHttpContextAccessor accessor) : ICurrentUserService
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public string? UserId => User?.FindFirstValue(ClaimTypes.NameIdentifier)
                          ?? User?.FindFirstValue("sub");

    public string? Role => User?.FindFirstValue("role");

    public string? Market => accessor.HttpContext?.Request.Headers["X-Market"].FirstOrDefault()?.ToLower();

    public string? XApp => accessor.HttpContext?.Request.Headers["X-App"].FirstOrDefault()?.ToLower();

    public string? IpAddress =>
        accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;
}
