namespace Dimes.Domain.Entities;

/// <summary>Append-only record of every lifecycle transition (and other notable actions),
/// across both ChangeRequests and Observations. <see cref="EntityId"/> is a loose polymorphic
/// reference, not a foreign key.</summary>
public class AuditEvent : Entity
{
    public AuditEntityType EntityType { get; set; }
    public Guid EntityId { get; set; }

    public Guid ActorId { get; set; }
    public Actor Actor { get; set; } = default!;

    public string? FromStatus { get; set; }
    public string? ToStatus { get; set; }
    public required string Action { get; set; }
    public string? Reason { get; set; }

    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}
