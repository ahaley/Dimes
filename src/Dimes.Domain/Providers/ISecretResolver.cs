namespace Dimes.Domain.Providers;

/// <summary>Resolves a secret reference (stored on provider configs) to its actual value. Keeps API
/// keys / tokens out of the database — the DB holds only the reference.
///
/// The returned value is always the secret's <em>contents</em>, never a path: an implementation backed by
/// files (see <c>ConfigurationSecretResolver</c>) reads them and returns what it read, so callers never
/// touch the filesystem. Implementations throw rather than returning null when a reference is configured
/// but unreadable, so a broken mount can't masquerade as "not configured".</summary>
public interface ISecretResolver
{
    string? Resolve(string? secretRef);
}
