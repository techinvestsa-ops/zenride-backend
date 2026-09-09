using Izigo.Application.Common.Interfaces;
using Izigo.Application.Features.Places.Dtos;
using Izigo.Application.Features.Places.Queries;
using Izigo.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Izigo.Application.Features.Places.Commands;

// ── POST /places ──────────────────────────────────────────────────────────────

public record AddPlaceCommand(string UserId, UpsertPlaceRequest Place) : IRequest<PlaceDto>;

public class AddPlaceHandler(IApplicationDbContext db) : IRequestHandler<AddPlaceCommand, PlaceDto>
{
    public async Task<PlaceDto> Handle(AddPlaceCommand req, CancellationToken ct)
    {
        var p = req.Place;
        var label = p.Label.ToLower();

        // 422 when home/office label is already taken
        if (label is "home" or "office")
        {
            var taken = await db.SavedPlaces
                .AnyAsync(x => x.UserId == req.UserId && x.Label == label, ct);
            if (taken)
                throw new ArgumentException(
                    $"VALIDATION_ERROR: You already have a '{label}' address. Edit it instead.");
        }

        var place = new SavedPlace
        {
            UserId       = req.UserId,
            Label        = label,
            Name         = p.Name.Trim(),
            Address      = p.Address.Trim(),
            Lat          = (decimal)p.Lat,
            Lng          = (decimal)p.Lng,
            PlaceId      = p.PlaceId,
            Building     = p.Building,
            Floor        = p.Floor,
            Instructions = p.Instructions,
            ContactPhone = p.ContactPhone,
            IsDefault    = p.IsDefault
        };

        db.SavedPlaces.Add(place);
        await db.SaveChangesAsync(ct);
        return GetPlacesHandler.Map(place);
    }
}

// ── PATCH /places/{id} ────────────────────────────────────────────────────────

public record UpdatePlaceCommand(string UserId, string PlaceId, UpsertPlaceRequest Place) : IRequest<PlaceDto>;

public class UpdatePlaceHandler(IApplicationDbContext db) : IRequestHandler<UpdatePlaceCommand, PlaceDto>
{
    public async Task<PlaceDto> Handle(UpdatePlaceCommand req, CancellationToken ct)
    {
        var place = await db.SavedPlaces
            .FirstOrDefaultAsync(p => p.Id == req.PlaceId && p.UserId == req.UserId, ct)
            ?? throw new KeyNotFoundException("Saved place not found.");

        var p = req.Place;
        place.Label        = p.Label.ToLower();
        place.Name         = p.Name.Trim();
        place.Address      = p.Address.Trim();
        place.Lat          = (decimal)p.Lat;
        place.Lng          = (decimal)p.Lng;
        place.PlaceId      = p.PlaceId;
        place.Building     = p.Building;
        place.Floor        = p.Floor;
        place.Instructions = p.Instructions;
        place.ContactPhone = p.ContactPhone;
        place.IsDefault    = p.IsDefault;
        place.UpdatedAt    = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return GetPlacesHandler.Map(place);
    }
}

// ── DELETE /places/{id} ───────────────────────────────────────────────────────

public record DeletePlaceCommand(string UserId, string PlaceId) : IRequest;

public class DeletePlaceHandler(IApplicationDbContext db) : IRequestHandler<DeletePlaceCommand>
{
    public async Task Handle(DeletePlaceCommand req, CancellationToken ct)
    {
        var place = await db.SavedPlaces
            .FirstOrDefaultAsync(p => p.Id == req.PlaceId && p.UserId == req.UserId, ct)
            ?? throw new KeyNotFoundException("Saved place not found.");

        db.SavedPlaces.Remove(place);
        await db.SaveChangesAsync(ct);
    }
}
