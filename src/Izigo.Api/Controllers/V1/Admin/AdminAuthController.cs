using Izigo.Application.Features.Admin.Auth.Commands;
using Izigo.Application.Features.Admin.Auth.Dtos;
using Izigo.Application.Features.Admin.Auth.Queries;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1.Admin;

/// <summary>
/// Handles admin authentication: login, two-factor authentication, token refresh,
/// session management, password management, and accepting staff invitations.
/// </summary>
[Route("api/v1/admin")]
public class AdminAuthController : AdminBaseController
{
    // ── Login & 2FA ───────────────────────────────────────────────────────────

    /// <summary>Authenticates an admin user with email and password.</summary>
    [AllowAnonymous]
    [HttpPost("auth/login")]
    public async Task<IActionResult> Login([FromBody] AdminLoginRequest req, CancellationToken ct)
    {
        var result = await Mediator.Send(new AdminLoginCommand(req.Email, req.Password), ct);

        if (result.ErrorCode is not null)
        {
            var status = result.ErrorCode == "ACCOUNT_BLOCKED" ? 403 : 401;
            return StatusCode(status, new { success = false, error = new { code = result.ErrorCode } });
        }

        if (result.Challenge is not null)
            return Ok(new { success = true, data = result.Challenge });

        return Ok(new { success = true, data = result.Bundle });
    }

    /// <summary>Verifies the TOTP or backup code to complete 2FA login.</summary>
    [AllowAnonymous]
    [HttpPost("auth/2fa/verify")]
    public async Task<IActionResult> Verify2Fa([FromBody] TwoFaVerifyRequest req, CancellationToken ct)
    {
        var result = await Mediator.Send(new Verify2FaCommand(req.ChallengeToken, req.Code), ct);

        if (result.ErrorCode is not null)
        {
            var status = result.ErrorCode is "2FA_EXPIRED" or "2FA_LOCKED" ? 403 : 401;
            return StatusCode(status, new { success = false, error = new { code = result.ErrorCode } });
        }

        return Ok(new { success = true, data = result.Bundle });
    }

    /// <summary>Enrols the authenticated admin user in two-factor authentication.</summary>
    [Authorize(Policy = "AdminPolicy")]
    [HttpPost("auth/2fa/enroll")]
    public async Task<IActionResult> Enroll2Fa(CancellationToken ct)
    {
        var result = await Mediator.Send(new Enroll2FaCommand(StaffId), ct);

        if (result.ErrorCode is not null)
            return result.ErrorCode == "ALREADY_ENROLLED"
                ? Conflict(new { success = false, error = new { code = result.ErrorCode } })
                : BadRequest(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true, data = result.Dto });
    }

    /// <summary>Exchanges a refresh token for a new admin access token pair.</summary>
    [AllowAnonymous]
    [HttpPost("auth/refresh")]
    public async Task<IActionResult> Refresh([FromBody] AdminRefreshRequest req, CancellationToken ct)
    {
        var result = await Mediator.Send(new AdminRefreshCommand(req.RefreshToken), ct);

        if (result.ErrorCode is not null)
            return Unauthorized(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true, data = result.Bundle });
    }

    /// <summary>Revokes the current admin session and invalidates the refresh token.</summary>
    [Authorize(Policy = "AdminPolicy")]
    [HttpPost("auth/logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await Mediator.Send(new AdminLogoutCommand(StaffId, CurrentSessionToken), ct);
        return Ok(new { success = true });
    }

    // ── Admin Profile ─────────────────────────────────────────────────────────

    /// <summary>Returns the profile of the currently authenticated admin user.</summary>
    [Authorize(Policy = "AdminPolicy")]
    [HttpGet("me")]
    public async Task<IActionResult> GetMe(CancellationToken ct)
    {
        var dto = await Mediator.Send(new GetMeQuery(StaffId), ct);
        if (dto is null) return NotFound(new { success = false, error = new { code = "NOT_FOUND" } });
        return Ok(new { success = true, data = dto });
    }

    /// <summary>Changes the authenticated admin user's password.</summary>
    [Authorize(Policy = "AdminPolicy")]
    [HttpPost("me/password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        var result = await Mediator.Send(new ChangeMyPasswordCommand(
            StaffId, req.CurrentPassword, req.Password, req.PasswordConfirmation, CurrentSessionToken), ct);

        if (!result.Success)
            return BadRequest(new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true });
    }

    /// <summary>Returns all active sessions for the authenticated admin user.</summary>
    [Authorize(Policy = "AdminPolicy")]
    [HttpGet("me/sessions")]
    public async Task<IActionResult> GetSessions(CancellationToken ct)
    {
        var sessions = await Mediator.Send(new GetMySessionsQuery(StaffId, CurrentSessionToken), ct);
        return Ok(new { success = true, data = sessions });
    }

    /// <summary>Revokes all other active sessions, keeping only the current one.</summary>
    [Authorize(Policy = "AdminPolicy")]
    [HttpPost("me/sessions/revoke-others")]
    public async Task<IActionResult> RevokeOtherSessions(CancellationToken ct)
    {
        await Mediator.Send(new RevokeOtherSessionsCommand(StaffId, CurrentSessionToken), ct);
        return Ok(new { success = true });
    }

    // ── Password Reset ────────────────────────────────────────────────────────

    /// <summary>Initiates the admin forgot-password flow.</summary>
    [AllowAnonymous]
    [HttpPost("auth/password/forgot")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest req, CancellationToken ct)
    {
        await Mediator.Send(new ForgotPasswordCommand(req.Email), ct);
        return Ok(new { success = true }); // always 200
    }

    /// <summary>Resets the admin password using a verified reset token.</summary>
    [AllowAnonymous]
    [HttpPost("auth/password/reset")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest req, CancellationToken ct)
    {
        var result = await Mediator.Send(new ResetPasswordCommand(req.Token, req.Password), ct);
        if (!result.Success)
            return BadRequest(new { success = false, error = new { code = result.ErrorCode } });
        return Ok(new { success = true });
    }

    // ── Invitation ────────────────────────────────────────────────────────────

    /// <summary>Accepts a staff invitation and sets up the new admin account.</summary>
    [AllowAnonymous]
    [HttpPost("auth/accept-invite")]
    public async Task<IActionResult> AcceptInvite([FromBody] AcceptInviteRequest req, CancellationToken ct)
    {
        var result = await Mediator.Send(new AcceptInviteCommand(req.Token, req.Password), ct);

        if (!result.Success)
            return BadRequest(new { success = false, error = new { code = result.ErrorCode } });

        if (result.RequiresTwoFaEnrollment)
            return Ok(new { success = true, data = new { requires_2fa_enrollment = true } });

        return Ok(new { success = true });
    }
}
