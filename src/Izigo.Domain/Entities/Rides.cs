using Izigo.Domain.Common;
using Izigo.Domain.Enums;

namespace Izigo.Domain.Entities;

/// <summary>Localised reason picker for ride/package cancellations.</summary>
public class CancellationReason : BaseEntity
{
    public CancellationReason() => Id = EntityId.ForCancellationReason();

    public string Code { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Language { get; set; } = "fr";
    public string Audience { get; set; } = "rider";   // rider | driver | both
    public bool IsActive { get; set; } = true;
    public int Order { get; set; }
}

/// <summary>Match request when a rider cannot find a suitable co-ride listing.</summary>
public class CoRideRequest : BaseEntity
{
    public CoRideRequest() => Id = EntityId.ForCoRideRequest();

    public string RiderId { get; set; } = string.Empty;
    public decimal PickupLat { get; set; }
    public decimal PickupLng { get; set; }
    public string PickupLabel { get; set; } = string.Empty;
    public decimal DropoffLat { get; set; }
    public decimal DropoffLng { get; set; }
    public string DropoffLabel { get; set; } = string.Empty;
    public int SeatsNeeded { get; set; }
    public DateTime DepartureWindowFrom { get; set; }
    public DateTime DepartureWindowTo { get; set; }
    public CoRideRequestStatus Status { get; set; } = CoRideRequestStatus.Searching;
    public string? MatchedListingId { get; set; }
    public DateTime? MatchedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>Device diagnostics submitted by the driver app.</summary>
public class DeviceDiagnostic : BaseEntity
{
    public string DeviceId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public int? BatteryLevel { get; set; }
    public bool? LocationPermission { get; set; }
    public bool? BackgroundPermission { get; set; }
    public bool? MockLocationDetected { get; set; }
}
