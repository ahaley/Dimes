namespace Dimes.Domain.Entities;

/// <summary>The tracked unit of work. Its <see cref="Status"/> is driven only through the
/// lifecycle engine, which enforces guards and writes an <see cref="AuditEvent"/> per transition.</summary>
public class ChangeRequest : Entity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = default!;

    public required string Title { get; set; }
    public string? Description { get; set; }

    public ChangeKind Kind { get; set; }
    public ChangeStatus Status { get; set; } = ChangeStatus.Captured;
    public Priority Priority { get; set; } = Priority.None;

    public Guid CreatedByActorId { get; set; }
    public Actor CreatedBy { get; set; } = default!;

    public Guid? AssigneeActorId { get; set; }
    public Actor? Assignee { get; set; }

    /// <summary>Self-reference for the Duplicate / merged-into terminal state.</summary>
    public Guid? DuplicateOfId { get; set; }
    public ChangeRequest? DuplicateOf { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public ICollection<ScmLink> ScmLinks { get; set; } = new List<ScmLink>();

    /// <summary>Observations promoted into this change, kept attached as evidence.</summary>
    public ICollection<Observation> Evidence { get; set; } = new List<Observation>();
}
