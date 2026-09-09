using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Validators;
using Izigo.Application.Features.Auth.Dtos;
using Izigo.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Auth.Commands;

public record CheckAvailabilityCommand(string? Phone, string? Email, string Role)
    : IRequest<CheckAvailabilityDto>;

public class CheckAvailabilityHandler(IApplicationDbContext db)
    : IRequestHandler<CheckAvailabilityCommand, CheckAvailabilityDto>
{
    public async Task<CheckAvailabilityDto> Handle(CheckAvailabilityCommand req, CancellationToken ct)
    {
        if (!Enum.TryParse<UserRole>(req.Role, true, out var role))
            throw new ArgumentException("VALIDATION_ERROR: Invalid role.");

        if (string.IsNullOrWhiteSpace(req.Phone) && string.IsNullOrWhiteSpace(req.Email))
            throw new ArgumentException("VALIDATION_ERROR: Provide phone or email.");

        bool taken;
        if (!string.IsNullOrWhiteSpace(req.Phone))
        {
            PhoneValidator.Validate(req.Phone);
            taken = await db.Users.AnyAsync(u => u.Phone == req.Phone && u.Role == role, ct);
        }
        else
        {
            taken = await db.Users.AnyAsync(
                u => u.Email == req.Email!.Trim().ToLower() && u.Role == role, ct);
        }

        return new CheckAvailabilityDto(!taken);
    }
}
