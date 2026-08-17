using Dimes.Infrastructure.Providers;
using Microsoft.Extensions.Configuration;

namespace Dimes.Tests;

/// <summary>Secret-reference resolution, including the file-backed routes that exist for credentials an
/// operator holds as a file rather than a string — a Google service-account credentials JSON being the
/// case that motivated them (multi-line, embeds a PEM private key).</summary>
public sealed class ConfigurationSecretResolverTests : IDisposable
{
    private readonly List<string> _tempFiles = [];
    private readonly List<string> _environmentVariables = [];

    private static ConfigurationSecretResolver Resolver(params (string Key, string Value)[] settings) =>
        new(new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build());

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
        Assert.Null(Resolver().Resolve(secretRef));
    }

    [Fact]
    public void Resolve_UnknownReference_IsNull()
    {
        Assert.Null(Resolver().Resolve("NOT_CONFIGURED_ANYWHERE"));
    }

    [Fact]
    public void Resolve_ReadsLiteralFromConfiguration()
    {
        var resolver = Resolver(("Secrets:GEMINI_KEY", "sk-literal"));
        Assert.Equal("sk-literal", resolver.Resolve("GEMINI_KEY"));
    }

    [Fact]
    public void Resolve_ReadsFileContentsFromSecretFilesSection()
    {
        // The Vertex case: the operator has a service-account JSON on disk and points at it.
        var path = TempFile("""{"type":"service_account","project_id":"p"}""");
        var resolver = Resolver(("SecretFiles:VERTEX_CREDS", path));

        var resolved = resolver.Resolve("VERTEX_CREDS");

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
        SetEnvironment($"{reference}_FILE", path);

        Assert.Equal("""{"type":"service_account"}""", Resolver().Resolve(reference));
    }

    [Fact]
    public void Resolve_PrefersALiteralEnvironmentVariableOverItsFileSibling()
    {
        var path = TempFile("from-file");
        var reference = UniqueReference();
        SetEnvironment(reference, "from-env");
        SetEnvironment($"{reference}_FILE", path);

        Assert.Equal("from-env", Resolver().Resolve(reference));
    }

    [Fact]
    public void Resolve_ConfigurationWinsOverEnvironment_AcrossBothForms()
    {
        // Tier order is configuration then environment, so an operator overriding in appsettings is not
        // silently shadowed by a leftover variable in the shell.
        var envPath = TempFile("from-env-file");
        var configPath = TempFile("from-config-file");
        var reference = UniqueReference();
        SetEnvironment($"{reference}_FILE", envPath);

        Assert.Equal("from-config-file", Resolver(($"SecretFiles:{reference}", configPath)).Resolve(reference));
    }

    [Fact]
    public void Resolve_TrimsTrailingNewlineFromAFile()
    {
        // Editors and `echo` add one. Harmless inside JSON, but it silently corrupts a bare API key — the
        // vendor then rejects the request with a generic "invalid key" that never mentions whitespace.
        var path = TempFile("sk-from-file\n");
        var resolver = Resolver(("SecretFiles:OPENAI_KEY", path));

        Assert.Equal("sk-from-file", resolver.Resolve("OPENAI_KEY"));
    }

    [Fact]
    public void Resolve_PrefersALiteralOverAFile_SoExistingReferencesAreUnaffected()
    {
        var path = TempFile("from-file");
        var resolver = Resolver(("Secrets:KEY", "from-config"), ("SecretFiles:KEY", path));

        Assert.Equal("from-config", resolver.Resolve("KEY"));
    }

    [Fact]
    public void Resolve_MissingSecretFile_ThrowsNamingTheSetting()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"dimes-absent-{Guid.NewGuid():N}.json");
        var resolver = Resolver(("SecretFiles:VERTEX_CREDS", missing));

        // Not null: a configured-but-unreadable path would look identical to "no secret configured", and
        // the operator would hunt for a missing setting instead of fixing the mount.
        var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve("VERTEX_CREDS"));
        Assert.Contains("SecretFiles:VERTEX_CREDS", ex.Message);
        Assert.Contains(missing, ex.Message);
    }

    [Fact]
    public void Resolve_EmptySecretFilePath_ThrowsRatherThanReadingTheWorkingDirectory()
    {
        var resolver = Resolver(("SecretFiles:VERTEX_CREDS", "   "));

        var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve("VERTEX_CREDS"));
        Assert.Contains("empty path", ex.Message);
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
