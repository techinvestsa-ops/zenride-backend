using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Profile.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Profile.Queries;

public record GetEmergencyContactsQuery(string UserId) : IRequest<List<EmergencyContactDto>>;

public class GetEmergencyContactsHandler(IApplicationDbContext db)
    : IRequestHandler<GetEmergencyContactsQuery, List<EmergencyContactDto>>
{
    public async Task<List<EmergencyContactDto>> Handle(GetEmergencyContactsQuery req, CancellationToken ct)
        => await db.EmergencyContacts
            .Where(e => e.UserId == req.UserId)
            .OrderBy(e => e.CreatedAt)
            .Select(e => new EmergencyContactDto(e.Id, e.Name, e.Phone, e.Relationship, e.NotifyOnTripStart))
            .ToListAsync(ct);
}
