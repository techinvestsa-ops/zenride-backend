using Izigo.Application.Common.Interfaces;
using Izigo.Domain.Entities;
using Izigo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Izigo.Infrastructure.Services;

public class IdempotencyService(ApplicationDbContext db, IConfiguration config) : IIdempotencyService
{
    private TimeSpan Ttl => TimeSpan.FromHours(
        int.TryParse(config["Idempotency:TtlHours"], out var h) ? h : 24);

    public async Task<IdempotencyRecord?> GetAsync(string key, string? userId,
        CancellationToken ct = default)
        => await db.IdempotencyRecords
            .FirstOrDefaultAsync(r =>
                r.Key == key &&
                r.UserId == userId &&
                r.ExpiresAt > DateTime.UtcNow, ct);

    public async Task SaveAsync(string key, string? userId, string endpoint,
        string responseJson, int statusCode, CancellationToken ct = default)
    {
        // Skip if already recorded (concurrent requests with same key)
        if (await db.IdempotencyRecords.AnyAsync(
                r => r.Key == key && r.UserId == userId, ct))
            return;

        db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Key = key,
            UserId = userId,
            Endpoint = endpoint,
            ResponseJson = responseJson,
            StatusCode = statusCode,
            ExpiresAt = DateTime.UtcNow.Add(Ttl)
        });

        await db.SaveChangesAsync(ct);
    }
}
