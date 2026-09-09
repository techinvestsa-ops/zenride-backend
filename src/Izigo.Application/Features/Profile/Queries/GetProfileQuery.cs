using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Profile.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Profile.Queries;

public record GetProfileQuery(string UserId) : IRequest<UserProfileDto>;

public class GetProfileHandler(IApplicationDbContext db) : IRequestHandler<GetProfileQuery, UserProfileDto>
{
    public async Task<UserProfileDto> Handle(GetProfileQuery req, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([req.UserId], ct)
            ?? throw new KeyNotFoundException("User not found.");

        var wallet = await db.Wallets
            .Where(w => w.UserId == req.UserId)
            .Select(w => new { w.Balance, w.Currency })
            .FirstOrDefaultAsync(ct);

        return Map(user, wallet?.Balance ?? 0, wallet?.Currency ?? "XOF");
    }

    internal static UserProfileDto Map(Domain.Entities.User user, long balance, string currency) =>
        new(
            Id: user.Id,
            Role: user.Role.ToString().ToLower(),
            FirstName: user.FirstName,
            LastName: user.LastName,
            Phone: user.Phone,
            PhoneVerified: user.PhoneVerified,
            Email: user.Email,
            EmailVerified: user.EmailVerified,
            PhotoUrl: user.PhotoUrl,
            Language: user.Language,
            Gender: user.Gender,
            DateOfBirth: user.DateOfBirth?.ToString("yyyy-MM-dd"),
            CreatedAt: user.CreatedAt,
            Rating: user.Rating,
            WalletBalance: balance,
            Currency: currency
        );
}
