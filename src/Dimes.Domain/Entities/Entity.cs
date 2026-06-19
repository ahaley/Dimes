namespace Dimes.Domain.Entities;

/// <summary>Base for all persisted entities: a GUID identity and a creation timestamp.</summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
