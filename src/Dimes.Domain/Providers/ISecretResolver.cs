namespace Dimes.Domain.Providers;

/// <summary>Resolves a secret reference (stored on provider configs) to its actual value. Keeps API
/// keys / tokens out of the database — the DB holds only the reference.</summary>
public interface ISecretResolver
{
    string? Resolve(string? secretRef);
}
