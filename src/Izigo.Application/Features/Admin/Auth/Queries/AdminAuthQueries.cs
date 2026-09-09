using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Admin.Auth.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Roles = Izigo.Application.Features.Admin.Auth.AdminRoles;

namespace Izigo.Application.Features.Admin.Auth.Queries;

// ── GET /admin/me ────────────────────────────────────────────────────────────────

public record GetMeQuery(string StaffId) : IRequest<StaffDto?>;

public class GetMeHandler(IApplicationDbContext db)
    : IRequestHandler<GetMeQuery, StaffDto?>
{
    public async Task<StaffDto?> Handle(GetMeQuery request, CancellationToken ct)
    {
        var staff = await db.Staff
            .FirstOrDefaultAsync(s => s.Id == request.StaffId, ct);

        if (staff is null) return null;

        var role = Roles.Roles.TryGetValue(staff.RoleKey, out var r)
            ? new StaffRoleDto(r.Key, r.Label, r.Rank)
            : new StaffRoleDto(staff.RoleKey, staff.RoleKey, 0);

        var perms = Roles.GetEffectivePermissions(staff);

        return new StaffDto(
            staff.Id, staff.Name, staff.Email, staff.Phone,
            role, perms,
            new StaffPermissionOverridesDto(staff.GrantedPermissions, staff.RevokedPermissions),
            staff.Markets,
            staff.TwoFaEnabled,
            staff.MustChangePassword,
            staff.Status.ToString().ToLower());
    }
}

// ── GET /admin/me/sessions ───────────────────────────────────────────────────────

public record GetMySessionsQuery(string StaffId, string? CurrentSessionToken) : IRequest<List<StaffSessionDto>>;

public class GetMySessionsHandler(IApplicationDbContext db)
    : IRequestHandler<GetMySessionsQuery, List<StaffSessionDto>>
{
    public async Task<List<StaffSessionDto>> Handle(GetMySessionsQuery request, CancellationToken ct)
    {
        var sessions = await db.StaffRefreshTokens
            .Where(r => r.StaffId == request.StaffId && !r.IsRevoked && r.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync(ct);

        return sessions.Select(r => new StaffSessionDto(
            r.SessionToken ?? r.Id,
            r.DeviceInfo,
            r.IpAddress,
            null,                           // coarse location: not tracked
            r.UpdatedAt,
            r.SessionToken == request.CurrentSessionToken))
            .ToList();
    }
}
