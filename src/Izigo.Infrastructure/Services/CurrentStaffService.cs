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

    // Lazily loaded from JWT claim or DB
    private IReadOnlyList<string>? _permissions;
    private IReadOnlyList<string>? _markets;

    public IReadOnlyList<string> Permissions
    {
        get
        {
            if (_permissions is not null) return _permissions;
            _permissions = LoadPermissions();
            return _permissions;
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

    private List<string> LoadPermissions()
    {
        if (!IsAuthenticated || StaffId is null) return [];

        // Load from DB synchronously (we're in a service context — no async available here)
        var staff = db.Staff.AsNoTracking()
            .FirstOrDefault(s => s.Id == StaffId);

        return staff is null ? [] : [.. AdminRoles.GetEffectivePermissions(staff)];
    }
}
