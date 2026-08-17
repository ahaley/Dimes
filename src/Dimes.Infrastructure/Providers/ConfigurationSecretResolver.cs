using Dimes.Domain.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dimes.Infrastructure.Providers;

/// <summary>Pass-1 secret store. Resolves a reference to its value by four routes, in order, within the
/// namespace its <see cref="SecretPurpose"/> selects:
///
/// <list type="number">
/// <item><c>Secrets:{section}{ref}</c> in configuration — the literal value.</item>
/// <item><c>SecretFiles:{section}{ref}</c> in configuration — a <em>path</em>; the file's contents are the value.</item>
/// <item>an environment variable named <c>{prefix}{ref}</c> — the literal value.</item>
/// <item>an environment variable named <c>{prefix}{ref}_FILE</c> — a <em>path</em>; contents are the value.</item>
/// </list>
///
/// The file routes exist because some credentials are impractical as a single config or environment value:
/// a Google service-account credentials JSON is multi-line and embeds a PEM private key, so operators have
/// it as a file, not a string. They also match how self-hosted deployments actually mount secrets — Docker
/// secrets at <c>/run/secrets/…</c>, Kubernetes projected volumes — and keep a private key out of the
/// environment, where it would be visible in process listings, crash dumps and every child process. The
/// <c>_FILE</c> suffix is the convention the official Docker images use, so it needs no explaining.
///
/// Literal routes are checked before file routes at each tier, and configuration before environment.
///
/// **The section/prefix is a privilege boundary.** See <see cref="SecretPurpose"/> for why. Only
/// <see cref="SecretPurpose.Operator"/> resolves unprefixed; every reference a project Maintainer can type
/// into a form is confined to its own section, so naming the OIDC client secret on a provider config finds
/// nothing. Containment holds because the prefix is a fixed leading segment and a reference can only ever
/// extend it — configuration keys have no traversal, and an environment name is a suffix — so no reference,
/// however hostile, addresses anything above its own section. That is also why the reference itself is not
/// charset-restricted: there is nothing to escape from.
///
/// Keeps keys/tokens out of the database — the DB holds only the reference. A real secret-manager backend
/// can replace this behind the same interface.</summary>
public sealed class ConfigurationSecretResolver(
    IConfiguration configuration, ILogger<ConfigurationSecretResolver> logger) : ISecretResolver
{
    /// <summary>The environment-variable suffix that marks a path rather than a value.</summary>
    private const string FileSuffix = "_FILE";

    /// <summary>Configuration sub-section per purpose, under <c>Secrets:</c> / <c>SecretFiles:</c>.</summary>
    private static string ConfigSection(SecretPurpose purpose) => purpose switch
    {
        SecretPurpose.Operator => string.Empty,
        SecretPurpose.LlmProvider => "Llm:",
        SecretPurpose.Notification => "Notification:",
        SecretPurpose.Scm => "Scm:",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
    };

    /// <summary>Environment-variable prefix per purpose. Environment names can't nest, so the section
    /// separator becomes an underscore.</summary>
    private static string EnvironmentPrefix(SecretPurpose purpose) => purpose switch
    {
        SecretPurpose.Operator => string.Empty,
        SecretPurpose.LlmProvider => "DIMES_LLM_",
        SecretPurpose.Notification => "DIMES_NOTIFICATION_",
        SecretPurpose.Scm => "DIMES_SCM_",
        _ => throw new ArgumentOutOfRangeException(nameof(purpose)),
    };

    public string? Resolve(SecretPurpose purpose, string? secretRef)
    {
        if (string.IsNullOrWhiteSpace(secretRef))
        {
            return null;
        }

        var section = ConfigSection(purpose);
        var prefix = EnvironmentPrefix(purpose);

        if (configuration[$"Secrets:{section}{secretRef}"] is string configured)
        {
            return configured;
        }

        var fileSetting = $"SecretFiles:{section}{secretRef}";
        if (configuration[fileSetting] is string configuredPath)
        {
            return ReadSecretFile(configuredPath, fileSetting);
        }

        if (Environment.GetEnvironmentVariable($"{prefix}{secretRef}") is string fromEnvironment)
        {
            return fromEnvironment;
        }

        var pathVariable = $"{prefix}{secretRef}{FileSuffix}";
        if (Environment.GetEnvironmentVariable(pathVariable) is string envPath)
        {
            return ReadSecretFile(envPath, pathVariable);
        }

        WarnIfBoundUnprefixed(purpose, secretRef, section, prefix);
        return null;
    }

    /// <summary>Tell the operator, in the log, when a reference is bound the old unprefixed way.
    ///
    /// Namespacing broke every reference an existing deployment had already bound, and the symptom on its
    /// own is inscrutable: the provider simply stops authenticating. This names the exact setting to
    /// create.
    ///
    /// It goes to the log rather than the caller's exception on purpose. The reference name is chosen by a
    /// project Maintainer, so surfacing "that one exists" to them would rebuild — in miniature — the
    /// capability this whole change removes: an oracle for which secrets the host process holds. The
    /// operator reading server logs is the one who needs it, and they can already see the environment.</summary>
    private void WarnIfBoundUnprefixed(SecretPurpose purpose, string secretRef, string section, string prefix)
    {
        if (section.Length == 0)
        {
            return; // Operator references already resolve unprefixed; there is nothing to migrate.
        }

        var boundUnprefixed = configuration[$"Secrets:{secretRef}"] is not null
            || configuration[$"SecretFiles:{secretRef}"] is not null
            || Environment.GetEnvironmentVariable(secretRef) is not null
            || Environment.GetEnvironmentVariable($"{secretRef}{FileSuffix}") is not null;
        if (!boundUnprefixed)
        {
            return;
        }

        logger.LogWarning(
            "Secret reference '{SecretRef}' is bound at an unprefixed location, which is no longer resolved "
            + "for {Purpose} secrets, so it currently resolves to nothing. Move it to "
            + "'Secrets:{Section}{SecretRef}' (or environment variable '{Prefix}{SecretRef}'); the file "
            + "routes take the same prefix. Unprefixed names are reserved for operator-only secrets such as "
            + "the OIDC client secret, so that a name typed into a provider form cannot reach them.",
            secretRef, purpose, section, secretRef, prefix, secretRef);
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
