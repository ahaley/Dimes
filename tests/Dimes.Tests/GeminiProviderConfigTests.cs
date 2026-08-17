using Dimes.Api;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Providers;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>Save-time rules and settings round-tripping for the two Gemini provider types, plus model
/// discovery. These are the checks that make a misconfiguration fail at save with a readable message
/// instead of at first agent comment.</summary>
public sealed class GeminiProviderConfigTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ProjectService _projects;

    public GeminiProviderConfigTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();
        _projects = new ProjectService(_db, new MembershipResolver(_db));
    }

    private sealed class StubSecrets : ISecretResolver
    {
        // Asserts the purpose, not just the name — model discovery resolves a Maintainer-supplied
        // reference, so it must stay confined to the LLM namespace.
        public string? Resolve(SecretPurpose purpose, string? secretRef)
        {
            Assert.Equal(SecretPurpose.LlmProvider, purpose);
            return secretRef is null ? null : "resolved";
        }
    }

    /// <summary>An adapter that can enumerate models, standing in for the real HTTP catalog calls.</summary>
    private sealed class StubCatalogProvider(LlmProviderType type, params string[] ids)
        : ILlmProvider, ILlmModelCatalog
    {
        public LlmProviderType Type => type;

        public LlmConnection? Received { get; private set; }

        public Task<LlmCompletionResult> CompleteAsync(
            LlmCompletionRequest request, LlmConnection connection, CancellationToken ct = default)
            => Task.FromResult(new LlmCompletionResult(string.Empty));

        public Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(
            LlmConnection connection, CancellationToken ct = default)
        {
            Received = connection;
            return Task.FromResult<IReadOnlyList<LlmModelInfo>>(ids.Select(i => new LlmModelInfo(i)).ToList());
        }
    }

    /// <summary>An adapter with no catalog capability — the Vertex case.</summary>
    private sealed class StubPlainProvider(LlmProviderType type) : ILlmProvider
    {
        public LlmProviderType Type => type;
        public Task<LlmCompletionResult> CompleteAsync(
            LlmCompletionRequest request, LlmConnection connection, CancellationToken ct = default)
            => Task.FromResult(new LlmCompletionResult(string.Empty));
    }

    [Fact]
    public async Task CreateGeminiProvider_RequiresApiKeyReference()
    {
        var project = await _projects.CreateAsync(_db, new CreateProjectRequest("P", null), ct: Ct);

        // The Google AI endpoint always authenticates, so a missing key reference must fail at save.
        await Assert.ThrowsAsync<BadRequestException>(() => _projects.CreateLlmProviderAsync(
            project.Id,
            new CreateLlmProviderRequest(LlmProviderType.Gemini, "gemini", null, "gemini-x", "  "),
            Ct));

        var created = await _projects.CreateLlmProviderAsync(
            project.Id,
            new CreateLlmProviderRequest(LlmProviderType.Gemini, "gemini", null, "gemini-x", "GEMINI_KEY"),
            Ct);
        Assert.Equal("GEMINI_KEY", created.ApiKeySecretRef);
        // No provider-specific settings for this type — the JSON column stays empty rather than holding
        // a blob of nulls.
        Assert.Null(created.Settings);
    }

    [Fact]
    public async Task CreateVertexProvider_RequiresProjectAndLocation()
    {
        var project = await _projects.CreateAsync(_db, new CreateProjectRequest("P", null), ct: Ct);

        await Assert.ThrowsAsync<BadRequestException>(() => _projects.CreateLlmProviderAsync(project.Id, new CreateLlmProviderRequest(
                LlmProviderType.GeminiVertex, "vertex", null, "gemini-x", "VERTEX_CREDS",
                new LlmProviderSettingsDto(GcpProject: null, GcpLocation: "us-central1", false)), Ct));

        await Assert.ThrowsAsync<BadRequestException>(() => _projects.CreateLlmProviderAsync(project.Id, new CreateLlmProviderRequest(
                LlmProviderType.GeminiVertex, "vertex", null, "gemini-x", "VERTEX_CREDS",
                new LlmProviderSettingsDto("my-gcp-project", GcpLocation: "   ", false)), Ct));
    }

    [Fact]
    public async Task CreateVertexProvider_AcceptsApplicationDefaultCredentials_WithNoSecret()
    {
        var project = await _projects.CreateAsync(_db, new CreateProjectRequest("P", null), ct: Ct);

        // ADC is the preferred configuration precisely because no secret is stored, so a null key
        // reference has to be legal here even though it is not for the Google AI type.
        var created = await _projects.CreateLlmProviderAsync(project.Id, new CreateLlmProviderRequest(
                LlmProviderType.GeminiVertex, "vertex", null, "gemini-x", null,
                new LlmProviderSettingsDto("my-gcp-project", "europe-west4", UseApplicationDefaultCredentials: true)), Ct);

        Assert.Null(created.ApiKeySecretRef);
        Assert.Equal("my-gcp-project", created.Settings!.GcpProject);
        Assert.Equal("europe-west4", created.Settings.GcpLocation);
        Assert.True(created.Settings.UseApplicationDefaultCredentials);
    }

    [Fact]
    public async Task CreateVertexProvider_RejectsNeitherCredential_AndBothAtOnce()
    {
        var project = await _projects.CreateAsync(_db, new CreateProjectRequest("P", null), ct: Ct);

        // Neither: nothing to authenticate with.
        await Assert.ThrowsAsync<BadRequestException>(() => _projects.CreateLlmProviderAsync(project.Id, new CreateLlmProviderRequest(
                LlmProviderType.GeminiVertex, "vertex", null, "gemini-x", null,
                new LlmProviderSettingsDto("p", "us-central1", UseApplicationDefaultCredentials: false)), Ct));

        // Both: refused rather than silently preferring one, so the operator can't be wrong about which
        // credential is in use.
        await Assert.ThrowsAsync<BadRequestException>(() => _projects.CreateLlmProviderAsync(project.Id, new CreateLlmProviderRequest(
                LlmProviderType.GeminiVertex, "vertex", null, "gemini-x", "VERTEX_CREDS",
                new LlmProviderSettingsDto("p", "us-central1", UseApplicationDefaultCredentials: true)), Ct));
    }

    [Fact]
    public async Task UpdateLlmProvider_RoundTripsSettings_AndClearsThemWhenOmitted()
    {
        var project = await _projects.CreateAsync(_db, new CreateProjectRequest("P", null), ct: Ct);
        var created = await _projects.CreateLlmProviderAsync(project.Id, new CreateLlmProviderRequest(
                LlmProviderType.GeminiVertex, "vertex", null, "gemini-x", null,
                new LlmProviderSettingsDto("p1", "us-central1", true)), Ct);

        var moved = await _projects.UpdateLlmProviderAsync(created.Id, new UpdateLlmProviderRequest(
                LlmProviderType.GeminiVertex, "vertex", null, "gemini-x", null, Enabled: true,
                new LlmProviderSettingsDto("p2", "asia-northeast1", true)), Ct);
        Assert.Equal("p2", moved.Settings!.GcpProject);
        Assert.Equal("asia-northeast1", moved.Settings.GcpLocation);

        // Switching the config to a type with no settings drops the blob rather than leaving stale
        // Vertex coordinates behind on the row.
        var retyped = await _projects.UpdateLlmProviderAsync(created.Id, new UpdateLlmProviderRequest(
                LlmProviderType.Gemini, "gemini", null, "gemini-x", "GEMINI_KEY", Enabled: true), Ct);
        Assert.Null(retyped.Settings);
    }

    [Fact]
    public async Task ListModels_ReturnsSortedIds_AndResolvesTheSecretByReference()
    {
        var stub = new StubCatalogProvider(LlmProviderType.Gemini, "gemini-z", "gemini-a");
        var catalog = new LlmModelCatalogService([stub], new StubSecrets());

        var models = await catalog.ListModelsAsync(new ListLlmModelsRequest(LlmProviderType.Gemini, null, "GEMINI_KEY"), Ct);

        Assert.Equal(["gemini-a", "gemini-z"], models.Select(m => m.Id));
        // The probe resolves the credential from the secret store exactly as a real call does, so the
        // endpoint never becomes a way to pass a raw key through the API.
        Assert.Equal("resolved", stub.Received!.ApiKey);
    }

    [Fact]
    public async Task ListModels_ForAnEndpointWithoutACatalog_SaysSoInsteadOfFailingOpaquely()
    {
        var catalog = new LlmModelCatalogService(
            [new StubPlainProvider(LlmProviderType.GeminiVertex)], new StubSecrets());

        var ex = await Assert.ThrowsAsync<BadRequestException>(() => catalog.ListModelsAsync(new ListLlmModelsRequest(
                LlmProviderType.GeminiVertex, null, null,
                new LlmProviderSettingsDto("p", "us-central1", true)), Ct));

        Assert.Contains("do not support model discovery", ex.Message);
    }

    [Fact]
    public async Task ListModels_AppliesThePerTypeBaseUrlPolicy()
    {
        var catalog = new LlmModelCatalogService(
            [new StubCatalogProvider(LlmProviderType.Gemini, "gemini-x")], new StubSecrets());

        // Discovery goes through the same validation as a real call, so it can't be used as an
        // unvalidated outbound-request primitive.
        await Assert.ThrowsAsync<BadRequestException>(() => catalog.ListModelsAsync(
            new ListLlmModelsRequest(LlmProviderType.Gemini, "https://attacker.example.com", "GEMINI_KEY"),
            Ct));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ not json")]
    public void ParseSettings_TolerantOfMissingOrCorruptJson(string? json)
    {
        // A row whose settings blob was hand-edited badly must still be readable and editable in the UI,
        // otherwise the operator has no way to fix it. Required values are caught by save-time validation.
        var settings = LlmProviderSettings.Parse(json);

        Assert.Null(settings.GcpProject);
        Assert.False(settings.UseApplicationDefaultCredentials);
    }

    [Fact]
    public void SettingsWithNothingSet_SerializeToNull()
    {
        Assert.Null(new LlmProviderSettings().ToJson());
        Assert.NotNull(new LlmProviderSettings { GcpProject = "p" }.ToJson());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
