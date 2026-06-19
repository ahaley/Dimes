namespace Dimes.Domain.Entities;

/// <summary>A configured capture source (SDK, Seq, …) behind the one ingestion interface.
/// Secrets are referenced, never stored inline.</summary>
public class ObservationSource : Entity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = default!;

    public ObservationSourceType Type { get; set; }
    public required string Name { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Source-specific configuration as JSON (e.g. Seq query URL, sampling).</summary>
    public string? ConfigJson { get; set; }

    /// <summary>Reference to a secret in the secret store — not the secret itself.</summary>
    public string? SecretRef { get; set; }

    public ICollection<Observation> Observations { get; set; } = new List<Observation>();
}
