namespace Dimes.Domain.Entities;

/// <summary>Top-level container. First-class even in single-tenant self-host, since roles,
/// sources, and provider configs are all scoped per project.</summary>
public class Project : Entity
{
    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>Whether the source-control feature is surfaced for this project. When off, change
    /// requests hide their Source control section. Defaults to on so existing projects are unchanged.</summary>
    public bool SourceControlEnabled { get; set; } = true;

    /// <summary>Archived projects are retained (with all their changes/observations/audit) but hidden
    /// from active lists. Soft-delete equivalent — reversible via unarchive.</summary>
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
    public ICollection<ChangeRequest> ChangeRequests { get; set; } = new List<ChangeRequest>();
    public ICollection<Observation> Observations { get; set; } = new List<Observation>();
    public ICollection<ObservationSource> ObservationSources { get; set; } = new List<ObservationSource>();
}
