using System.Text.Json;
using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Entities;
using Izigo.Domain.Enums;

namespace Izigo.Infrastructure.Services;

public class AuditService(IApplicationDbContext db) : IAuditService
{
    public async Task RecordAsync(
        string staffId, string staffName, AuditAction action,
        string? targetType = null, string? targetId = null, string? reason = null,
        object? before = null, object? after = null, string? requestId = null,
        string? ipAddress = null, string market = "ci", CancellationToken ct = default)
    {
        var entry = new AuditLog
        {
            ActorId    = staffId,
            ActorName  = staffName,
            Action     = action,
            TargetType = targetType,
            TargetId   = targetId,
            Reason     = reason,
            IpAddress  = ipAddress,
            RequestId  = requestId,
            Market     = market,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before),
            AfterJson  = after  is null ? null : JsonSerializer.Serialize(after)
        };

        db.AuditLogs.Add(entry);
        // Caller is responsible for SaveChangesAsync (must be in same transaction)
        await Task.CompletedTask;
    }
}
