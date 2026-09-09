namespace Izigo.Domain.Common;

public abstract class BaseEntity
{
    // Default: time-ordered unique ID with no prefix.
    // Entities that need a contract-visible prefix override this in their own constructor.
    public string Id { get; protected set; } =
        Guid.CreateVersion7().ToString("N").ToLower()[..20];

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public abstract class AuditableEntity : BaseEntity
{
    public string? CreatedBy { get; set; }
    public string? UpdatedBy { get; set; }
}
