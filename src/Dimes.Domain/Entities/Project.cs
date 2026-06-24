namespace Dimes.Domain.Entities;

/// <summary>Top-level container. First-class even in single-tenant self-host, since roles,
/// sources, and provider configs are all scoped per project.</summary>
public class Project : Entity
{
    public required string Name { get; set; }
    public string? Description { get; set; }

    /// <summary>Short, immutable, human-readable key (e.g. "DIMES") — the prefix of a change's display
    /// id "KEY-NUMBER". Unique across projects; required on create, but nullable in the DB so existing
    /// rows can be backfilled on startup. Stored uppercase (^[A-Z][A-Z0-9]{1,5}$).</summary>
    public string? Key { get; set; }

    /// <summary>Whether the source-control feature is surfaced for this project. When off, change
    /// requests hide their Source control section. Defaults to on so existing projects are unchanged.</summary>
    public bool SourceControlEnabled { get; set; } = true;

    /// <summary>When on, the project presents as human-only: AI-agent affordances and agentic verbiage
    /// (Capture Assist, agent commentary, the add-agent form, agent members) are hidden in the UI.
    /// Defaults to off so existing projects are unchanged.</summary>
    public bool HumanOnly { get; set; }

    /// <summary>Archived projects are retained (with all their changes/observations/audit) but hidden
    /// from active lists. Soft-delete equivalent — reversible via unarchive.</summary>
    public bool IsArchived { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }

    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
    public ICollection<ChangeRequest> ChangeRequests { get; set; } = new List<ChangeRequest>();
    public ICollection<Observation> Observations { get; set; } = new List<Observation>();
    public ICollection<ObservationSource> ObservationSources { get; set; } = new List<ObservationSource>();
    public ICollection<SystemInstruction> SystemInstructions { get; set; } = new List<SystemInstruction>();
}
