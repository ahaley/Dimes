using Dimes.Domain.Providers;
using Dimes.Infrastructure.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dimes.Tests;

/// <summary>Secret-reference resolution: the four routes, the file-backed ones that exist for credentials
/// an operator holds as a file rather than a string (a Google service-account credentials JSON being the
/// case that motivated them), and the per-purpose namespace that keeps a Maintainer-supplied reference
/// from addressing an operator-only secret.</summary>
public sealed class ConfigurationSecretResolverTests : IDisposable
{
    private readonly List<string> _tempFiles = [];
    private readonly List<string> _environmentVariables = [];

    private static ConfigurationSecretResolver Resolver(params (string Key, string Value)[] settings) =>
        new(new ConfigurationBuilder()
                .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
                .Build(),
            NullLogger<ConfigurationSecretResolver>.Instance);

    private string TempFile(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dimes-secret-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, contents);
        _tempFiles.Add(path);
        return path;
    }

    /// <summary>Environment variables are process-global, so tests use unique names and clear them.</summary>
    private void SetEnvironment(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        _environmentVariables.Add(name);
    }

    private static string UniqueReference() => $"DIMES_TEST_CREDS_{Guid.NewGuid():N}";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_BlankReference_IsNull(string? secretRef)
    {
        Assert.Null(Resolver().Resolve(SecretPurpose.LlmProvider, secretRef));
    }

    [Fact]
    public void Resolve_UnknownReference_IsNull()
    {
        Assert.Null(Resolver().Resolve(SecretPurpose.LlmProvider, "NOT_CONFIGURED_ANYWHERE"));
    }

    [Fact]
    public void Resolve_ReadsLiteralFromConfiguration()
    {
        var resolver = Resolver(("Secrets:Llm:GEMINI_KEY", "sk-literal"));
        Assert.Equal("sk-literal", resolver.Resolve(SecretPurpose.LlmProvider, "GEMINI_KEY"));
    }

    [Fact]
    public void Resolve_ReadsFileContentsFromSecretFilesSection()
    {
        // The Vertex case: the operator has a service-account JSON on disk and points at it.
        var path = TempFile("""{"type":"service_account","project_id":"p"}""");
        var resolver = Resolver(("SecretFiles:Llm:VERTEX_CREDS", path));

        var resolved = resolver.Resolve(SecretPurpose.LlmProvider, "VERTEX_CREDS");

        Assert.Equal("""{"type":"service_account","project_id":"p"}""", resolved);
        // Callers get contents, never a path — nothing downstream touches the filesystem.
        Assert.DoesNotContain(path, resolved);
    }

    [Fact]
    public void Resolve_ReadsFileContentsFromFileSuffixedEnvironmentVariable()
    {
        // The convention the official Docker images use: <name>_FILE names a path where <name> would have
        // named the value. This is the route a Docker/Kubernetes secret mount takes.
        var path = TempFile("""{"type":"service_account"}""");
        var reference = UniqueReference();
        SetEnvironment($"DIMES_LLM_{reference}_FILE", path);

        Assert.Equal(
            """{"type":"service_account"}""",
            Resolver().Resolve(SecretPurpose.LlmProvider, reference));
    }

    [Fact]
    public void Resolve_PrefersALiteralEnvironmentVariableOverItsFileSibling()
    {
        var path = TempFile("from-file");
        var reference = UniqueReference();
        SetEnvironment($"DIMES_LLM_{reference}", "from-env");
        SetEnvironment($"DIMES_LLM_{reference}_FILE", path);

        Assert.Equal("from-env", Resolver().Resolve(SecretPurpose.LlmProvider, reference));
    }

    [Fact]
    public void Resolve_ConfigurationWinsOverEnvironment_AcrossBothForms()
    {
        // Tier order is configuration then environment, so an operator overriding in appsettings is not
        // silently shadowed by a leftover variable in the shell.
        var envPath = TempFile("from-env-file");
        var configPath = TempFile("from-config-file");
        var reference = UniqueReference();
        SetEnvironment($"DIMES_LLM_{reference}_FILE", envPath);

        Assert.Equal(
            "from-config-file",
            Resolver(($"SecretFiles:Llm:{reference}", configPath)).Resolve(SecretPurpose.LlmProvider, reference));
    }

    [Fact]
    public void Resolve_TrimsTrailingNewlineFromAFile()
    {
        // Editors and `echo` add one. Harmless inside JSON, but it silently corrupts a bare API key — the
        // vendor then rejects the request with a generic "invalid key" that never mentions whitespace.
        var path = TempFile("sk-from-file\n");
        var resolver = Resolver(("SecretFiles:Llm:OPENAI_KEY", path));

        Assert.Equal("sk-from-file", resolver.Resolve(SecretPurpose.LlmProvider, "OPENAI_KEY"));
    }

    [Fact]
    public void Resolve_PrefersALiteralOverAFile()
    {
        var path = TempFile("from-file");
        var resolver = Resolver(("Secrets:Llm:KEY", "from-config"), ("SecretFiles:Llm:KEY", path));

        Assert.Equal("from-config", resolver.Resolve(SecretPurpose.LlmProvider, "KEY"));
    }

    [Fact]
    public void Resolve_MissingSecretFile_ThrowsNamingTheSetting()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"dimes-absent-{Guid.NewGuid():N}.json");
        var resolver = Resolver(("SecretFiles:Llm:VERTEX_CREDS", missing));

        // Not null: a configured-but-unreadable path would look identical to "no secret configured", and
        // the operator would hunt for a missing setting instead of fixing the mount.
        var ex = Assert.Throws<InvalidOperationException>(
            () => resolver.Resolve(SecretPurpose.LlmProvider, "VERTEX_CREDS"));
        Assert.Contains("SecretFiles:Llm:VERTEX_CREDS", ex.Message);
        Assert.Contains(missing, ex.Message);
    }

    [Fact]
    public void Resolve_EmptySecretFilePath_ThrowsRatherThanReadingTheWorkingDirectory()
    {
        var resolver = Resolver(("SecretFiles:Llm:VERTEX_CREDS", "   "));

        var ex = Assert.Throws<InvalidOperationException>(
            () => resolver.Resolve(SecretPurpose.LlmProvider, "VERTEX_CREDS"));
        Assert.Contains("empty path", ex.Message);
    }

    // ----- The namespace boundary ------------------------------------------
    // These are the point of the purpose parameter: a reference name typed into a provider form must not
    // be able to address a secret the operator bound for something else.

    /// <summary>The attack this closes. A project Maintainer names the OIDC client secret's reference on
    /// a provider config pointed at a host they control; the adapter would then send it there as a bearer
    /// token. Resolving under a Maintainer-facing purpose must find nothing.</summary>
    [Fact]
    public void Resolve_MaintainerSuppliedReference_CannotReachAnOperatorSecret()
    {
        var resolver = Resolver(("Secrets:OIDC_CLIENT_SECRET", "the-client-secret"));

        Assert.Null(resolver.Resolve(SecretPurpose.LlmProvider, "OIDC_CLIENT_SECRET"));
        Assert.Null(resolver.Resolve(SecretPurpose.Notification, "OIDC_CLIENT_SECRET"));
        Assert.Null(resolver.Resolve(SecretPurpose.Scm, "OIDC_CLIENT_SECRET"));
        // ...while the operator's own call site still resolves it, unprefixed and unchanged.
        Assert.Equal("the-client-secret", resolver.Resolve(SecretPurpose.Operator, "OIDC_CLIENT_SECRET"));
    }

    /// <summary>The environment fallback is the wider hole of the two — it reaches every variable of the
    /// host process, not just what appsettings declares.</summary>
    [Fact]
    public void Resolve_MaintainerSuppliedReference_CannotReachABareEnvironmentVariable()
    {
        var reference = UniqueReference();
        SetEnvironment(reference, "aws-secret-access-key");

        Assert.Null(Resolver().Resolve(SecretPurpose.LlmProvider, reference));
        Assert.Equal("aws-secret-access-key", Resolver().Resolve(SecretPurpose.Operator, reference));
    }

    /// <summary>Each Maintainer-facing purpose gets its own section, so the sections don't leak into one
    /// another either. This is defence in depth rather than a distinct privilege — a Maintainer can create
    /// both kinds of config — but it keeps the boundary in place if the roles ever diverge.</summary>
    [Fact]
    public void Resolve_PurposesDoNotShareASection()
    {
        var resolver = Resolver(("Secrets:Llm:SHARED", "llm-value"), ("Secrets:Scm:SHARED", "scm-value"));

        Assert.Equal("llm-value", resolver.Resolve(SecretPurpose.LlmProvider, "SHARED"));
        Assert.Equal("scm-value", resolver.Resolve(SecretPurpose.Scm, "SHARED"));
        Assert.Null(resolver.Resolve(SecretPurpose.Notification, "SHARED"));
    }

    /// <summary>A hostile reference can extend its own section but never climb out of it: configuration
    /// keys have no traversal and the prefix is a fixed leading segment. This is why the reference is not
    /// charset-restricted — there is nothing to escape from.</summary>
    [Theory]
    [InlineData("../OIDC_CLIENT_SECRET")]
    [InlineData("..:OIDC_CLIENT_SECRET")]
    [InlineData(":OIDC_CLIENT_SECRET")]
    public void Resolve_ReferenceCannotEscapeItsSection(string hostileReference)
    {
        var resolver = Resolver(
            ("Secrets:OIDC_CLIENT_SECRET", "the-client-secret"),
            ("Secrets:Llm:GEMINI_KEY", "sk-literal"));

        Assert.Null(resolver.Resolve(SecretPurpose.LlmProvider, hostileReference));
    }

    public void Dispose()
    {
        foreach (var name in _environmentVariables)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
        foreach (var path in _tempFiles)
        {
            File.Delete(path);
        }
    }
}
