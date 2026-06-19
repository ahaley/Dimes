namespace Dimes.Domain.Entities;

/// <summary>Top-level container. First-class even in single-tenant self-host, since roles,
/// sources, and provider configs are all scoped per project.</summary>
public class Project : Entity
{
    public required string Name { get; set; }
    public string? Description { get; set; }

    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
    public ICollection<ChangeRequest> ChangeRequests { get; set; } = new List<ChangeRequest>();
    public ICollection<Observation> Observations { get; set; } = new List<Observation>();
    public ICollection<ObservationSource> ObservationSources { get; set; } = new List<ObservationSource>();
}
