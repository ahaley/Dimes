namespace Dimes.Domain.Entities;

/// <summary>One export of a project's In-Development column as an executable work order. The exported
/// markdown embeds a capability token that lets the executing agent post its results back without a Dimes
/// session — the same posture as an <see cref="ObservationSource"/> id on the capture endpoint, but scoped
/// to this one export and carrying only the exporting actor's authority.</summary>
public class WorkOrder : Entity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = default!;

    /// <summary>Whose authority the reported results carry. Ingest attributes every comment and link to
    /// this actor and re-resolves their membership on each report, so removing the member kills the token.</summary>
    public Guid ExportedByActorId { get; set; }
    public Actor ExportedBy { get; set; } = default!;

    /// <summary>The generated download name — the human handle for this export in the UI and in the
    /// provenance line of the comments ingest posts.</summary>
    public required string FileName { get; set; }

    /// <summary>SHA-256 (hex) of the capability token. The token itself is emitted once, into the exported
    /// markdown, and never persisted — so a database read can't be replayed against the ingest endpoint,
    /// and <see cref="Entity.Id"/> stays safe to expose in DTOs.</summary>
    public required string TokenHash { get; set; }

    /// <summary>The token stops being accepted after this. Bounds the blast radius of a leaked work-order
    /// file, which agents plausibly commit alongside the code they were asked to write.</summary>
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddDays(30);

    public DateTimeOffset? LastReportedAt { get; set; }

    public ICollection<WorkOrderItem> Items { get; set; } = new List<WorkOrderItem>();
}
