using Izigo.Application.Features.Admin.Auth;
using Izigo.Application.Features.Admin.StaffManagement.Commands;
using Izigo.Application.Features.Admin.StaffManagement.Queries;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Izigo.Api.Controllers.V1.Admin;

[Route("api/v1/admin")]
[Authorize(Policy = "AdminPolicy")]
public class StaffAdminController : AdminBaseController
{
    // Every route re-checks permission AND rank per the spec.
    // actor.rank > target.rank; CANNOT_TARGET_SELF; LAST_SUPER_ADMIN; reason required on destructive routes.

    private string ActorRoleKey => CurrentStaff.Permissions.Count > 0
        ? User.FindFirst("role")?.Value ?? "read_only"
        : "read_only";

    // ── Staff directory ───────────────────────────────────────────────────────

    [HttpGet("staff")]
    public async Task<IActionResult> GetStaff(
        [FromQuery] string? status, [FromQuery] string? role,
        [FromQuery] string? market, [FromQuery] bool? twofa,
        [FromQuery] string? q,
        [FromQuery] int page = 1, [FromQuery] int per_page = 25,
        CancellationToken ct = default)
    {
        var check = CheckPermission("staff.manage");
        if (check is not null) return check;

        return Ok(await Mediator.Send(new GetStaffListQuery(
            StaffId, status, role, market, twofa, q, page, per_page), ct));
    }

    [HttpGet("staff/{id}")]
    public async Task<IActionResult> GetStaffMember(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("staff.manage");
        if (check is not null) return check;

        var result = await Mediator.Send(new GetStaffDetailQuery(id), ct);
        if (result is null) return NotFound(new { success = false, error = new { code = "STAFF_NOT_FOUND" } });
        return Ok(result);
    }

    [HttpPost("staff/invite")]
    public async Task<IActionResult> InviteStaff(
        [FromBody] InviteStaffRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("staff.manage");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Role))
            return BadRequest(new { success = false, error = new { code = "EMAIL_AND_ROLE_REQUIRED" } });

        var result = await Mediator.Send(new InviteStaffCommand(
            req.Name, req.Email, req.Phone, req.Role, req.Markets ?? [],
            StaffId, ActorRoleKey, CurrentStaff.Email ?? StaffId), ct);

        if (!result.Success)
            return result.ErrorCode is "EMAIL_ALREADY_EXISTS" or "INVALID_ROLE"
                ? Conflict(new { success = false, error = new { code = result.ErrorCode } })
                : StatusCode(403, new { success = false, error = new { code = result.ErrorCode } });

        return Ok(new { success = true, data = result.Data });
    }

    [HttpPatch("staff/{id}")]
    public async Task<IActionResult> UpdateStaff(string id,
        [FromBody] UpdateStaffRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("staff.manage");
        if (check is not null) return check;

        var result = await Mediator.Send(new UpdateStaffDetailsCommand(
            id, StaffId, ActorRoleKey, CurrentStaff.Email ?? StaffId,
            req.Name, req.Email, req.Phone, req.Markets), ct);

        return result.ErrorCode switch
        {
            null               => Ok(new { success = true }),
            "STAFF_NOT_FOUND"  => NotFound(new { success = false, error = new { code = result.ErrorCode } }),
            "CANNOT_TARGET_SELF" or "INSUFFICIENT_RANK"
                               => StatusCode(403, new { success = false, error = new { code = result.ErrorCode } }),
            _                  => UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } })
        };
    }

    [HttpPut("staff/{id}/role")]
    public async Task<IActionResult> SetStaffRole(string id,
        [FromBody] SetRoleRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("staff.manage");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new ChangeStaffRoleCommand(
            id, StaffId, ActorRoleKey, CurrentStaff.Email ?? StaffId,
            req.Role, req.Reason, req.KeepOverrides), ct);

        return result.ErrorCode switch
        {
            null                => Ok(new { success = true, data = result.Data }),
            "STAFF_NOT_FOUND"   => NotFound(new { success = false, error = new { code = result.ErrorCode } }),
            "LAST_SUPER_ADMIN"  => Conflict(new { success = false, error = new { code = result.ErrorCode } }),
            "CANNOT_TARGET_SELF" or "INSUFFICIENT_RANK" or "INVALID_ROLE"
                                => StatusCode(403, new { success = false, error = new { code = result.ErrorCode } }),
            _                   => UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } })
        };
    }

    [HttpPut("staff/{id}/permissions")]
    public async Task<IActionResult> SetStaffPermissions(string id,
        [FromBody] SetPermissionsRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("staff.manage");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new SetStaffPermissionsCommand(
            id, StaffId, ActorRoleKey, CurrentStaff.Email ?? StaffId,
            req.Granted ?? [], req.Revoked ?? [], req.Reason), ct);

        return result.ErrorCode switch
        {
            null                         => Ok(new { success = true, data = result.Data }),
            "STAFF_NOT_FOUND"            => NotFound(new { success = false, error = new { code = result.ErrorCode } }),
            "CANNOT_GRANT_WHAT_YOU_LACK" => StatusCode(403, new { success = false, error = new { code = result.ErrorCode } }),
            "CANNOT_TARGET_SELF" or "INSUFFICIENT_RANK"
                                         => StatusCode(403, new { success = false, error = new { code = result.ErrorCode } }),
            _                            => UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } })
        };
    }

    [HttpPost("staff/{id}/password/reset-link")]
    public async Task<IActionResult> SendPasswordResetLink(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("staff.password");
        if (check is not null) return check;

        var result = await Mediator.Send(new SendPasswordResetLinkCommand(
            id, StaffId, ActorRoleKey, CurrentStaff.Email ?? StaffId), ct);

        return result.ErrorCode switch
        {
            null               => Ok(new { success = true, data = result.Data }),
            "STAFF_NOT_FOUND"  => NotFound(new { success = false, error = new { code = result.ErrorCode } }),
            _                  => StatusCode(403, new { success = false, error = new { code = result.ErrorCode } })
        };
    }

    [HttpPost("staff/{id}/password/set")]
    public async Task<IActionResult> SetPassword(string id,
        [FromBody] SetPasswordRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("staff.password");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new SetStaffPasswordCommand(
            id, StaffId, ActorRoleKey, CurrentStaff.Email ?? StaffId,
            req.Password, req.Reason), ct);

        return result.ErrorCode switch
        {
            null                  => Ok(new { success = true }),
            "STAFF_NOT_FOUND"     => NotFound(new { success = false, error = new { code = result.ErrorCode } }),
            "PASSWORD_TOO_SHORT"  => BadRequest(new { success = false, error = new { code = result.ErrorCode } }),
            _                     => StatusCode(403, new { success = false, error = new { code = result.ErrorCode } })
        };
    }

    [HttpPost("staff/{id}/2fa/reset")]
    public async Task<IActionResult> Reset2Fa(string id,
        [FromBody] ReasonRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("staff.revoke");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new Reset2FaCommand(
            id, StaffId, ActorRoleKey, CurrentStaff.Email ?? StaffId, req.Reason), ct);

        return result.ErrorCode switch
        {
            null               => Ok(new { success = true }),
            "STAFF_NOT_FOUND"  => NotFound(new { success = false, error = new { code = result.ErrorCode } }),
            _                  => StatusCode(403, new { success = false, error = new { code = result.ErrorCode } })
        };
    }

    [HttpPost("staff/{id}/sessions/revoke")]
    public async Task<IActionResult> RevokeSessions(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("staff.revoke");
        if (check is not null) return check;

        var result = await Mediator.Send(new RevokeStaffSessionsCommand(
            id, StaffId, ActorRoleKey, CurrentStaff.Email ?? StaffId), ct);

        return result.ErrorCode switch
        {
            null               => Ok(new { success = true, data = result.Data }),
            "STAFF_NOT_FOUND"  => NotFound(new { success = false, error = new { code = result.ErrorCode } }),
            _                  => StatusCode(403, new { success = false, error = new { code = result.ErrorCode } })
        };
    }

    [HttpPost("staff/{id}/suspend")]
    public async Task<IActionResult> SuspendStaff(string id,
        [FromBody] SuspendRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("staff.revoke");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new SuspendStaffCommand(
            id, StaffId, ActorRoleKey, CurrentStaff.Email ?? StaffId,
            req.Reason, req.Until), ct);

        return result.ErrorCode switch
        {
            null                => Ok(new { success = true }),
            "STAFF_NOT_FOUND"   => NotFound(new { success = false, error = new { code = result.ErrorCode } }),
            "LAST_SUPER_ADMIN"  => Conflict(new { success = false, error = new { code = result.ErrorCode } }),
            _                   => StatusCode(403, new { success = false, error = new { code = result.ErrorCode } })
        };
    }

    [HttpPost("staff/{id}/block")]
    public async Task<IActionResult> BlockStaff(string id,
        [FromBody] ReasonRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("staff.revoke");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new BlockStaffCommand(
            id, StaffId, ActorRoleKey, CurrentStaff.Email ?? StaffId, req.Reason), ct);

        return result.ErrorCode switch
        {
            null                => Ok(new { success = true }),
            "STAFF_NOT_FOUND"   => NotFound(new { success = false, error = new { code = result.ErrorCode } }),
            "LAST_SUPER_ADMIN"  => Conflict(new { success = false, error = new { code = result.ErrorCode } }),
            _                   => StatusCode(403, new { success = false, error = new { code = result.ErrorCode } })
        };
    }

    [HttpPost("staff/{id}/reinstate")]
    public async Task<IActionResult> ReinstateStaff(string id,
        [FromBody] ReasonRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("staff.revoke");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new ReinstateStaffCommand(
            id, StaffId, ActorRoleKey, CurrentStaff.Email ?? StaffId, req.Reason), ct);

        return result.ErrorCode switch
        {
            null               => Ok(new { success = true }),
            "STAFF_NOT_FOUND"  => NotFound(new { success = false, error = new { code = result.ErrorCode } }),
            _                  => StatusCode(403, new { success = false, error = new { code = result.ErrorCode } })
        };
    }

    [HttpDelete("staff/{id}")]
    public async Task<IActionResult> DeleteStaff(string id, CancellationToken ct = default)
    {
        var check = CheckPermission("staff.manage");
        if (check is not null) return check;

        var result = await Mediator.Send(new DeleteStaffCommand(
            id, StaffId, ActorRoleKey, CurrentStaff.Email ?? StaffId), ct);

        return result.ErrorCode switch
        {
            null                  => Ok(new { success = true }),
            "STAFF_NOT_FOUND"     => NotFound(new { success = false, error = new { code = result.ErrorCode } }),
            "HAS_AUDIT_HISTORY"   => Conflict(new { success = false, error = new { code = result.ErrorCode } }),
            _                     => StatusCode(403, new { success = false, error = new { code = result.ErrorCode } })
        };
    }

    // ── Roles & Permissions ───────────────────────────────────────────────────

    [HttpGet("roles")]
    public async Task<IActionResult> GetRoles(CancellationToken ct = default)
    {
        var check = CheckPermission("staff.manage");
        if (check is not null) return check;

        return Ok(await Mediator.Send(new GetRolesQuery(ActorRoleKey), ct));
    }

    [HttpPut("roles/{key}")]
    public async Task<IActionResult> UpsertRole(string key,
        [FromBody] UpdateRoleRequest req, CancellationToken ct = default)
    {
        var check = CheckPermission("staff.manage");
        if (check is not null) return check;

        if (string.IsNullOrWhiteSpace(req.Reason))
            return BadRequest(new { success = false, error = new { code = "REASON_REQUIRED" } });

        var result = await Mediator.Send(new UpdateRolePermissionsCommand(
            key, req.Permissions ?? [], req.Reason,
            StaffId, ActorRoleKey, CurrentStaff.Email ?? StaffId), ct);

        return result.ErrorCode switch
        {
            null                => Ok(new { success = true, data = result.Data }),
            "ROLE_NOT_FOUND"    => NotFound(new { success = false, error = new { code = result.ErrorCode } }),
            "INSUFFICIENT_RANK" => StatusCode(403, new { success = false, error = new { code = result.ErrorCode } }),
            _                   => UnprocessableEntity(new { success = false, error = new { code = result.ErrorCode } })
        };
    }

    [HttpGet("permissions")]
    public async Task<IActionResult> GetPermissions(CancellationToken ct = default)
    {
        var check = CheckPermission("staff.manage");
        if (check is not null) return check;

        return Ok(await Mediator.Send(new GetPermissionCatalogueQuery(), ct));
    }
}

// ── Request shapes ────────────────────────────────────────────────────────────

public record InviteStaffRequest(string Name, string Email, string? Phone,
    string Role, string[]? Markets);
public record UpdateStaffRequest(string? Name, string? Email,
    string? Phone, string[]? Markets);
public record SetRoleRequest(string Role, string Reason, bool KeepOverrides = false);
public record SetPermissionsRequest(string[]? Granted, string[]? Revoked, string Reason);
public record SetPasswordRequest(string Password, string Reason);
public record SuspendRequest(string Reason, DateTime? Until);
public record UpdateRoleRequest(string[]? Permissions, string Reason);
public record ReasonRequest(string Reason);
