namespace Izigo.Application.Features.Places.Dtos;

// Exact fields from the contract
public record PlaceDto(
    string Id,
    string Label,        // home | office | custom
    string Name,
    string Address,
    double Lat,
    double Lng,
    string? PlaceId,
    string? Building,
    string? Floor,
    string? Instructions,
    string? ContactPhone,
    bool IsDefault
);

public record UpsertPlaceRequest(
    string Label,
    string Name,
    string Address,
    double Lat,
    double Lng,
    string? PlaceId,
    string? Building,
    string? Floor,
    string? Instructions,
    string? ContactPhone,
    bool IsDefault
);
