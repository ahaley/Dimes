namespace Dimes.Domain.Entities;

/// <summary>A manual, read-only link from a change to a source-control repo or PR, with a
/// snapshot of pulled context. Pass-1 has no build actions and no automated state sync.</summary>
public class ScmLink : Entity
{
    public Guid ChangeRequestId { get; set; }
    public ChangeRequest ChangeRequest { get; set; } = default!;

    public ScmProviderType Provider { get; set; } = ScmProviderType.GitHub;
    public required string Url { get; set; }

    /// <summary>Context pulled from the SCM provider (e.g. PR title/description), as text/JSON.</summary>
    public string? ContextSnapshot { get; set; }
}
