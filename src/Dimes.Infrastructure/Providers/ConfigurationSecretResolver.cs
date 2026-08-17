using Dimes.Domain.Providers;
using Microsoft.Extensions.Configuration;

namespace Dimes.Infrastructure.Providers;

/// <summary>Pass-1 secret store. Resolves a reference to its value by four routes, in order:
///
/// <list type="number">
/// <item><c>Secrets:{ref}</c> in configuration — the literal value.</item>
/// <item><c>SecretFiles:{ref}</c> in configuration — a <em>path</em>; the file's contents are the value.</item>
/// <item>an environment variable named <c>{ref}</c> — the literal value.</item>
/// <item>an environment variable named <c>{ref}_FILE</c> — a <em>path</em>; contents are the value.</item>
/// </list>
///
/// The file routes exist because some credentials are impractical as a single config or environment value:
/// a Google service-account credentials JSON is multi-line and embeds a PEM private key, so operators have
/// it as a file, not a string. They also match how self-hosted deployments actually mount secrets — Docker
/// secrets at <c>/run/secrets/…</c>, Kubernetes projected volumes — and keep a private key out of the
/// environment, where it would be visible in process listings, crash dumps and every child process. The
/// <c>_FILE</c> suffix is the convention the official Docker images use, so it needs no explaining.
///
/// Literal routes are checked before file routes at each tier, so any reference that resolved before this
/// existed still resolves the same way.
///
/// Keeps keys/tokens out of the database — the DB holds only the reference. A real secret-manager backend
/// can replace this behind the same interface.</summary>
public sealed class ConfigurationSecretResolver(IConfiguration configuration) : ISecretResolver
{
    /// <summary>The environment-variable suffix that marks a path rather than a value.</summary>
    private const string FileSuffix = "_FILE";

    public string? Resolve(string? secretRef)
    {
        if (string.IsNullOrWhiteSpace(secretRef))
        {
            return null;
        }

        if (configuration[$"Secrets:{secretRef}"] is string configured)
        {
            return configured;
        }

        if (configuration[$"SecretFiles:{secretRef}"] is string configuredPath)
        {
            return ReadSecretFile(configuredPath, $"SecretFiles:{secretRef}");
        }

        if (Environment.GetEnvironmentVariable(secretRef) is string fromEnvironment)
        {
            return fromEnvironment;
        }

        var pathVariable = $"{secretRef}{FileSuffix}";
        return Environment.GetEnvironmentVariable(pathVariable) is string envPath
            ? ReadSecretFile(envPath, pathVariable)
            : null;
    }

    /// <summary>Read a secret file, failing loudly if it can't be read.
    ///
    /// Deliberately not "return null on error": a configured-but-unreadable path would then look exactly
    /// like "no secret configured", and the operator would go hunting for a missing setting instead of
    /// fixing a mount path or file permission. The message names the setting that pointed here.
    ///
    /// The trailing newline is trimmed. Editors and <c>echo</c> add one, and while it is harmless inside a
    /// JSON credential it silently corrupts a bare API key — the vendor rejects the request with a generic
    /// "invalid key" that gives no hint the value merely has whitespace on the end.</summary>
    private static string ReadSecretFile(string path, string setting)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"Secret setting '{setting}' is set to an empty path.");
        }

        try
        {
            return File.ReadAllText(path).TrimEnd('\r', '\n');
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new InvalidOperationException(
                $"Could not read the secret file at '{path}' named by '{setting}': {ex.Message}", ex);
        }
    }
}
