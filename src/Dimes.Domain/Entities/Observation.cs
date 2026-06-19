namespace Dimes.Domain.Entities;

/// <summary>A captured signal in the observation inbox. Aggregates by <see cref="Fingerprint"/>;
/// once promoted it links to the <see cref="ChangeRequest"/> it became evidence for.</summary>
public class Observation : Entity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = default!;

    public Guid SourceId { get; set; }
    public ObservationSource Source { get; set; } = default!;

    public ObservationKind Kind { get; set; }
    public ObservationStatus Status { get; set; } = ObservationStatus.New;

    /// <summary>Raw signal payload as JSON (message, stack, friction details, …).</summary>
    public required string Payload { get; set; }

    /// <summary>Context metadata as JSON: route, app version, role, breadcrumbs, device.</summary>
    public string? ContextMetadata { get; set; }

    /// <summary>Dedup/cluster key (e.g. exception type + stack signature).</summary>
    public string? Fingerprint { get; set; }
    public int OccurrenceCount { get; set; } = 1;
    public DateTimeOffset FirstSeen { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Set when promoted: the change this observation is attached to as evidence.</summary>
    public Guid? ChangeRequestId { get; set; }
    public ChangeRequest? ChangeRequest { get; set; }
}
