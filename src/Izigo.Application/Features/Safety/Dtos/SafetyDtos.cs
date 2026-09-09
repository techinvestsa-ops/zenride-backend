namespace Izigo.Application.Features.Safety.Dtos;

public record SosDto(
    string SosId,
    string Status,
    string Source,
    double Lat,
    double Lng,
    string? TripId,
    string EmergencyNumber,   // spec: "returns emergency_number to dial"
    DateTime CreatedAt
);

public record SharedTripDto(
    string Code,
    string Status,
    string PickupLabel,
    string DropoffLabel,
    string? DriverFirstName,
    string? VehiclePlate,
    string? EncodedPolyline,
    DateTime? EstimatedArrival
);

public record ShareTokenDto(string ShareUrl, DateTime ExpiresAt);

// Requests
public record TriggerSosRequest(
    string Type,           // accident | harassment | medical | other
    string Source,         // spec: source=rider|driver in request body
    double Lat,
    double Lng,
    string? TripId,
    string? Note
);

public record ReportTripRequest(
    string Category,
    string Description,
    string[]? Attachments  // spec: attachments[]
);

public record SafetyCheckinRequest(string? Note);
