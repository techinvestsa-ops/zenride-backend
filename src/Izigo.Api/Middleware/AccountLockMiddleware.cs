using System.Security.Claims;
using System.Text.Json;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Models;
using Izigo.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Api.Middleware;

/// <summary>
/// A suspended account can sign in, but every call except the appeal thread is refused.
/// A blocked account is refused entirely, apart from logout.
/// </summary>
public class AccountLockMiddleware(RequestDelegate next)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public async Task InvokeAsync(HttpContext context, IApplicationDbContext db)
    {
        if (context.User?.Identity?.IsAuthenticated != true || IsAllowed(context.Request.Path))
        {
            await next(context);
            return;
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.User.FindFirstValue("sub");
        if (string.IsNullOrWhiteSpace(userId))
        {
            await next(context);
            return;
        }

        var user = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => new { u.Status, u.SuspensionReason })
            .FirstOrDefaultAsync(context.RequestAborted);

        if (user is null || user.Status == UserStatus.Active)
        {
            await next(context);
            return;
        }

        if (user.Status == UserStatus.Suspended && IsAppeal(context.Request.Path))
        {
            await next(context);
            return;
        }

        var code = user.Status == UserStatus.Blocked ? "ACCOUNT_BLOCKED" : "ACCOUNT_LOCKED";
        var message = user.Status == UserStatus.Blocked
            ? "This account is blocked. Contact support."
            : (user.SuspensionReason ?? "This account is suspended. You can only send an appeal.");

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            JsonSerializer.Serialize(ApiResponse.Fail(code, message), JsonOptions));
    }

    private static bool IsAllowed(PathString path)
    {
        var value = path.Value ?? "";
        return value.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/api/v1/auth/refresh", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/api/v1/auth/logout", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/api/v1/auth/login", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/api/v1/auth/otp", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/api/v1/auth/biometric/login", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAppeal(PathString path)
    {
        var value = path.Value ?? "";
        return value.StartsWith("/api/v1/appeals", StringComparison.OrdinalIgnoreCase);
    }
}
