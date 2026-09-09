using Izigo.Application.Common.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1.Admin;

[ApiController]
[Produces("application/json")]
public abstract class AdminBaseController : ControllerBase
{
    protected IMediator Mediator =>
        HttpContext.RequestServices.GetRequiredService<IMediator>();

    protected ICurrentStaffService CurrentStaff =>
        HttpContext.RequestServices.GetRequiredService<ICurrentStaffService>();

    protected string StaffId => CurrentStaff.StaffId!;

    protected string Market =>
        Request.Headers["X-Market"].FirstOrDefault() ?? "ci";

    /// <summary>
    /// Returns 403 FORBIDDEN if the current staff does not have the specified permission.
    /// Returns null if the permission check passes (caller may continue).
    /// </summary>
    protected IActionResult? CheckPermission(string perm) =>
        CurrentStaff.HasPermission(perm)
            ? null
            : StatusCode(403, new
            {
                success = false,
                error   = new { code = "FORBIDDEN", message = $"Missing permission: {perm}" }
            });

    /// <summary>Standard 403 when an app token is used on an admin route.</summary>
    protected static IActionResult NotStaff() =>
        new ObjectResult(new
        {
            success = false,
            error   = new { code = "NOT_STAFF", message = "Admin credentials required." }
        }) { StatusCode = 403 };

    /// <summary>Session token stored in the "X-Session-Token" header or "session_token" claim.</summary>
    protected string? CurrentSessionToken =>
        Request.Headers["X-Session-Token"].FirstOrDefault()
     ?? User.FindFirst("session_token")?.Value;
}
