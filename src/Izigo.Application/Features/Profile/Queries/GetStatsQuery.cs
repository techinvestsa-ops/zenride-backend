using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Profile.Dtos;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Profile.Queries;

public record GetStatsQuery(string UserId) : IRequest<RiderStatsDto>;

public class GetStatsHandler(IApplicationDbContext db) : IRequestHandler<GetStatsQuery, RiderStatsDto>
{
    // Approximate CO₂ saving: 0.15 kg/km versus average car, scaled for a shared trip
    private const decimal Co2PerKm = 0.12m;

    public async Task<RiderStatsDto> Handle(GetStatsQuery req, CancellationToken ct)
    {
        var user = await db.Users
            .Where(u => u.Id == req.UserId)
            .Select(u => new { u.CreatedAt, u.Rating })
            .FirstOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("User not found.");

        var totalTrips = await db.Trips
            .CountAsync(t => t.RiderId == req.UserId &&
                             t.JobState == JobState.Completed, ct);

        var totalDistanceM = await db.Trips
            .Where(t => t.RiderId == req.UserId && t.JobState == JobState.Completed)
            .SumAsync(t => (int?)t.DistanceM ?? 0, ct);

        var wallet = await db.Wallets
            .Where(w => w.UserId == req.UserId)
            .Select(w => new { w.Balance })
            .FirstOrDefaultAsync(ct);

        return new RiderStatsDto(
            TotalTrips: totalTrips,
            WalletBalance: wallet?.Balance ?? 0,
            Rating: user.Rating,
            MemberSince: user.CreatedAt,
            Co2SavedKg: Math.Round(totalDistanceM / 1000m * Co2PerKm, 2)
        );
    }
}
