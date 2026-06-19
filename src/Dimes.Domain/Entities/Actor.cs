namespace Dimes.Domain.Entities;

/// <summary>The load-bearing identity. Humans and agents share this table so authorship,
/// assignment, and (future) approval are uniform — an Agent is just an Actor whose work is
/// driven by an <see cref="LlmProviderConfig"/>.</summary>
public class Actor : Entity
{
    public required string DisplayName { get; set; }
    public ActorType Type { get; set; }

    /// <summary>Human actors: their login/identity email.</summary>
    public string? Email { get; set; }

    /// <summary>Agent actors: the LLM endpoint they are "configured as".</summary>
    public Guid? LlmProviderConfigId { get; set; }
    public LlmProviderConfig? LlmProviderConfig { get; set; }

    /// <summary>Archived actors are kept (so their authorship/assignment history stays valid) but
    /// hidden from active management lists. Archiving is the soft alternative to deletion for actors
    /// that are referenced and therefore can't be hard-deleted.</summary>
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
}
