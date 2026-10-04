using System.Collections.Concurrent;

namespace Izigo.Api.Services;

/// <summary>Tracks how many admin SignalR connections each staff member has open.</summary>
public sealed class AdminStaffPresenceTracker
{
    private readonly ConcurrentDictionary<string, int> _connections = new();

    /// <returns>True when this is the first connection (staff just came online).</returns>
    public bool TryAddConnection(string staffId)
    {
        var count = _connections.AddOrUpdate(staffId, 1, (_, c) => c + 1);
        return count == 1;
    }

    /// <returns>True when the last connection closed (staff went offline).</returns>
    public bool TryRemoveConnection(string staffId)
    {
        while (true)
        {
            if (!_connections.TryGetValue(staffId, out var count))
                return false;

            if (count <= 1)
            {
                if (_connections.TryRemove(staffId, out _))
                    return true;
                continue;
            }

            if (_connections.TryUpdate(staffId, count - 1, count))
                return false;
        }
    }

    public bool IsOnline(string staffId) =>
        _connections.TryGetValue(staffId, out var count) && count > 0;
}
