namespace Dimes.Domain.Entities;

/// <summary>A project-scoped, operator-editable block of instruction text consumed by a specific
/// feature (today: the In-Development export "work order" guidance — see
/// <see cref="SystemInstructionKind.ExportWorkOrder"/>). Stored in the database so the guidance can be
/// tuned at runtime instead of hardcoded. At most one row per (Project, Kind); when a row is absent the
/// consuming feature falls back to its built-in default.</summary>
public class SystemInstruction : Entity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = default!;

    public SystemInstructionKind Kind { get; set; }

    /// <summary>The instruction text. For <see cref="SystemInstructionKind.ExportWorkOrder"/> this is the
    /// guidance body (the Objective + How-to-work sections) inserted between the generated title and the
    /// generated change list.</summary>
    public required string Content { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
