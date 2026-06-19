namespace Dimes.Domain.Entities;

/// <summary>Configuration for a source-control provider behind the <c>ScmProvider</c> interface.
/// Pass-1 ships GitHub only. The access token is referenced via the secret store.</summary>
public class ScmProviderConfig : Entity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = default!;

    public ScmProviderType Type { get; set; } = ScmProviderType.GitHub;
    public required string Name { get; set; }

    /// <summary>Reference to the access token in the secret store — never the token itself.</summary>
    public string? TokenSecretRef { get; set; }

    public bool Enabled { get; set; } = true;
}
