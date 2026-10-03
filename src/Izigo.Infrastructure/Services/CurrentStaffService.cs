using System.Security.Claims;
using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Admin.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Infrastructure.Services;

public class CurrentStaffService(
    IHttpContextAccessor accessor,
    IApplicationDbContext db) : ICurrentStaffService
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public string? StaffId =>
        Principal?.FindFirstValue("staff_id")
     ?? Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
     ?? Principal?.FindFirstValue("sub");

    public string? Email => Principal?.FindFirstValue("email");

    public bool IsAuthenticated =>
        Principal?.Identity?.IsAuthenticated == true &&
        Principal?.FindFirstValue("is_staff") == "true";

    // Lazily loaded from the staff row, which is the rank source of truth.
    private bool _loaded;
    private string? _roleKey;
    private IReadOnlyList<string>? _permissions;
    private IReadOnlyList<string>? _markets;

    public string? RoleKey
    {
        get
        {
            EnsureLoaded();
            return _roleKey;
        }
    }

    public IReadOnlyList<string> Permissions
    {
        get
        {
            EnsureLoaded();
            return _permissions ?? [];
        }
    }

    public IReadOnlyList<string> Markets
    {
        get
        {
            if (_markets is not null) return _markets;
            var marketsClaim = Principal?.FindFirstValue("markets");
            _markets = string.IsNullOrWhiteSpace(marketsClaim)
                ? []
                : marketsClaim.Split(',', StringSplitOptions.RemoveEmptyEntries);
            return _markets;
        }
    }

    public bool HasPermission(string permission) =>
        Permissions.Contains(permission);

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        if (!IsAuthenticated || StaffId is null)
        {
            _permissions = [];
            return;
        }

        var staff = db.Staff.AsNoTracking().FirstOrDefault(s => s.Id == StaffId);
        if (staff is null)
        {
            _permissions = [];
            return;
        }

        _roleKey = staff.RoleKey;
        _permissions = [.. AdminRoles.GetEffectivePermissions(staff)];
    }
}
