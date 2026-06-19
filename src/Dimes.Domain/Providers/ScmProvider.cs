namespace Dimes.Domain.Providers;

/// <summary>Context pulled from a source-control item (repo / PR / issue) for a linked change.</summary>
public sealed record ScmContext(string? Title, string? Description, string? State, string Raw);

/// <summary>The thin SCM seam. Pass-1 ships GitHub (read-only). Callers select by <see cref="Type"/>.</summary>
public interface IScmProvider
{
    ScmProviderType Type { get; }

    /// <summary>Fetch read-only context for a repo/PR/issue URL. <paramref name="token"/> is optional
    /// (public resources need none). Returns null when the URL isn't understood or can't be fetched.</summary>
    Task<ScmContext?> FetchContextAsync(string url, string? token, CancellationToken ct = default);
}
