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

    /// <summary>Self-reference for Epic composition: when set, this change is a composed child of the
    /// referenced Epic (a change whose <see cref="Kind"/> is <see cref="ChangeKind.Epic"/>). Null for a
    /// standalone change or an Epic itself. Composition is independent of the Duplicate self-reference.</summary>
    public Guid? ParentChangeRequestId { get; set; }
    public ChangeRequest? Parent { get; set; }

    /// <summary>The change requests composed under this one (only populated for an Epic).</summary>
    public ICollection<ChangeRequest> Children { get; set; } = new List<ChangeRequest>();

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Manual within-column order set by board drag-and-drop. 0 = unordered (the default),
    /// so a never-reordered column falls back to newest-updated-first; a reordered column gets
    /// explicit 1..n. Scoped per (project, status).</summary>
    public int SortOrder { get; set; }

    /// <summary>Per-project sequential number (1, 2, 3 …) forming the display id "KEY-NUMBER" with the
    /// project's key. Assigned on create; nullable in the DB so existing rows can be backfilled on
    /// startup. Unique per project, never reused.</summary>
    public int? Number { get; set; }

    /// <summary>When this change was last accepted into Done (set on the In Review → Done transition,
    /// cleared on reopen). Drives the board's "recent vs. older" Done split. Null until first accepted.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public ICollection<ScmLink> ScmLinks { get; set; } = new List<ScmLink>();

    /// <summary>Observations promoted into this change, kept attached as evidence.</summary>
    public ICollection<Observation> Evidence { get; set; } = new List<Observation>();
}
