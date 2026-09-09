using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Profile.Dtos;
using Izigo.Application.Features.Profile.Queries;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Profile.Commands;

public record UpdateProfileCommand(
    string UserId,
    string? FirstName,
    string? LastName,
    string? Email,
    string? Language,
    string? Gender,
    string? DateOfBirth
) : IRequest<UserProfileDto>;

public class UpdateProfileHandler(IApplicationDbContext db) : IRequestHandler<UpdateProfileCommand, UserProfileDto>
{
    private static readonly string[] AllowedLanguages = ["fr", "en"];

    public async Task<UserProfileDto> Handle(UpdateProfileCommand req, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([req.UserId], ct)
            ?? throw new KeyNotFoundException("User not found.");

        // Email uniqueness — only if changed
        if (!string.IsNullOrWhiteSpace(req.Email) &&
            !string.Equals(req.Email.Trim(), user.Email, StringComparison.OrdinalIgnoreCase))
        {
            var emailTaken = await db.Users.AnyAsync(u =>
                u.Id != req.UserId &&
                u.Role == user.Role &&
                u.Email == req.Email.Trim().ToLower(), ct);

            if (emailTaken)
                throw new ArgumentException("VALIDATION_ERROR: Email is already in use.");

            user.Email = req.Email.Trim().ToLower();
            user.EmailVerified = false;   // must re-verify after change
        }

        if (!string.IsNullOrWhiteSpace(req.FirstName))
            user.FirstName = req.FirstName.Trim();

        if (!string.IsNullOrWhiteSpace(req.LastName))
            user.LastName = req.LastName.Trim();

        if (!string.IsNullOrWhiteSpace(req.Language))
        {
            if (!AllowedLanguages.Contains(req.Language.ToLower()))
                throw new ArgumentException("VALIDATION_ERROR: Unsupported language.");
            user.Language = req.Language.ToLower();
        }

        if (req.Gender != null)
            user.Gender = req.Gender;

        if (!string.IsNullOrWhiteSpace(req.DateOfBirth))
        {
            if (!DateOnly.TryParse(req.DateOfBirth, out var dob))
                throw new ArgumentException("VALIDATION_ERROR: Invalid date_of_birth format (use yyyy-MM-dd).");
            user.DateOfBirth = dob;
        }

        await db.SaveChangesAsync(ct);

        var wallet = await db.Wallets
            .Where(w => w.UserId == req.UserId)
            .Select(w => new { w.Balance, w.Currency })
            .FirstOrDefaultAsync(ct);

        return GetProfileHandler.Map(user, wallet?.Balance ?? 0, wallet?.Currency ?? "XOF");
    }
}
