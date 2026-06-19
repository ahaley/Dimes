using Dimes.Domain.Providers;
using Microsoft.Extensions.Configuration;

namespace Dimes.Infrastructure.Providers;

/// <summary>Pass-1 secret store: resolves a reference against configuration (<c>Secrets:{ref}</c>)
/// or an environment variable of the same name. Keeps keys/tokens out of the database. A real
/// secret-manager backend can replace this behind the same interface.</summary>
public sealed class ConfigurationSecretResolver(IConfiguration configuration) : ISecretResolver
{
    public string? Resolve(string? secretRef)
    {
        if (string.IsNullOrWhiteSpace(secretRef))
        {
            return null;
        }

        return configuration[$"Secrets:{secretRef}"]
               ?? Environment.GetEnvironmentVariable(secretRef);
    }
}
