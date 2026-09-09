using Izigo.Application.Common.Interfaces;
using Izigo.Application.Common.Validators;
using Izigo.Application.Features.Profile.Dtos;
using Izigo.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Profile.Commands;

// ── Add ───────────────────────────────────────────────────────────────────────

public record AddEmergencyContactCommand(
    string UserId,
    string Name,
    string Phone,
    string? Relationship,
    bool NotifyOnTripStart
) : IRequest<EmergencyContactDto>;

public class AddEmergencyContactHandler(IApplicationDbContext db)
    : IRequestHandler<AddEmergencyContactCommand, EmergencyContactDto>
{
    private const int MaxContacts = 5;

    public async Task<EmergencyContactDto> Handle(AddEmergencyContactCommand req, CancellationToken ct)
    {
        PhoneValidator.Validate(req.Phone, "phone");
        var count = await db.EmergencyContacts.CountAsync(e => e.UserId == req.UserId, ct);
        if (count >= MaxContacts)
            throw new InvalidOperationException($"CONFLICT: Maximum {MaxContacts} emergency contacts allowed.");

        var contact = new EmergencyContact
        {
            UserId = req.UserId,
            Name = req.Name.Trim(),
            Phone = req.Phone.Trim(),
            Relationship = req.Relationship?.Trim(),
            NotifyOnTripStart = req.NotifyOnTripStart
        };

        db.EmergencyContacts.Add(contact);
        await db.SaveChangesAsync(ct);

        return new EmergencyContactDto(contact.Id, contact.Name, contact.Phone,
            contact.Relationship, contact.NotifyOnTripStart);
    }
}

// ── Delete ────────────────────────────────────────────────────────────────────

public record DeleteEmergencyContactCommand(string UserId, string ContactId) : IRequest;

public class DeleteEmergencyContactHandler(IApplicationDbContext db)
    : IRequestHandler<DeleteEmergencyContactCommand>
{
    public async Task Handle(DeleteEmergencyContactCommand req, CancellationToken ct)
    {
        var contact = await db.EmergencyContacts
            .FirstOrDefaultAsync(e => e.Id == req.ContactId && e.UserId == req.UserId, ct)
            ?? throw new KeyNotFoundException("Emergency contact not found.");

        db.EmergencyContacts.Remove(contact);
        await db.SaveChangesAsync(ct);
    }
}
