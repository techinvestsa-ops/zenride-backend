namespace Izigo.Application.Features.Admin.Config.Dtos;

// ── Admin config mirror ───────────────────────────────────────────────────────────

public record DispatchConfigDto(
    string Market,
    int OfferTimeoutSeconds,
    int SearchRadiusM,
    string Strategy,
    int MaxConcurrentOffers,
    int LocationPingOnTripS,
    int LocationPingIdleS,
    string? UpdatedByStaffId,
    DateTime? UpdatedAt);

public record FeatureFlagDto(
    string Key,
    string Description,
    bool Enabled,
    int RolloutPct,
    string Scope);

public record IntegrationDto(
    string Key,
    string Label,
    bool IsConnected,
    string? LastError,
    DateTime? KeyLastRotatedAt);

// ── Request shapes ────────────────────────────────────────────────────────────────

public record UpdateDispatchConfigRequest(
    int OfferTimeoutSeconds,
    int SearchRadiusM,
    string Strategy,
    int MaxConcurrentOffers,
    int LocationPingOnTripS,
    int LocationPingIdleS,
    string Reason);

public record UpdateFeatureFlagRequest(
    bool Enabled,
    int? RolloutPct,
    string? Scope,
    string Reason);

public record UpdateMaintenanceRequest(
    string Mode,   // live | read_only | maintenance
    string? Message,
    string? App,   // rider | driver | all
    string Reason);

public record UpdateIntegrationRequest(
    string ApiKey,
    string Reason);
