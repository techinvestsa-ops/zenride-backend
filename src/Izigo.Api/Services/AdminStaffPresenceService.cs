using System.Collections.Concurrent;
using Izigo.Api.Hubs;
using Izigo.Application.Common.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Api.Services;

public sealed class AdminStaffPresenceService(
    IServiceScopeFactory scopes,
    IHubContext<AdminHub> hub)
{
    private static readonly ConcurrentDictionary<string, DateTime> LastPersisted = new();
    private static readonly TimeSpan PersistInterval = TimeSpan.FromMinutes(2);

    public async Task TouchAsync(string staffId, bool online, bool forcePersist, CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();

        var staff = await db.Staff.AsNoTracking()
            .Where(s => s.Id == staffId)
            .Select(s => new { s.Markets, s.LastActiveAt })
            .FirstOrDefaultAsync(ct);

        if (staff is null) return;

        var now = DateTime.UtcNow;
        var shouldPersist = forcePersist
            || !LastPersisted.TryGetValue(staffId, out var last)
            || now - last >= PersistInterval;

        if (shouldPersist)
        {
            await db.Staff.Where(s => s.Id == staffId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastActiveAt, now), ct);
            LastPersisted[staffId] = now;
        }

        var payload = new
        {
            staff_id       = staffId,
            online,
            last_active_at = now,
        };

        foreach (var market in staff.Markets)
        {
            var group = $"admin-{market.Trim().ToLowerInvariant()}";
            await hub.Clients.Group(group).SendAsync("staff.presence_changed", payload, ct);
        }
    }
}
