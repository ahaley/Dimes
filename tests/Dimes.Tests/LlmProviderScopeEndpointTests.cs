using System.Net;
using System.Net.Http.Json;
using Dimes.Api.Contracts;

namespace Dimes.Tests;

/// <summary>Endpoint-level cover for the two-sided authority on a scope move. Moving a provider both removes
/// it from one scope and adds it to another, so the caller must clear the bar for *both* — and only the HTTP
/// layer knows who the caller is. A service test can't see this: <c>MoveLlmProviderScopeAsync</c> is handed a
/// destination and trusts the controller to have authorized it.
///
/// The case that matters is a project Maintainer publishing their own provider website-wide. They legitimately
/// administer the source, so a one-sided check on the source alone would let it through.</summary>
public sealed class LlmProviderScopeEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;

    public LlmProviderScopeEndpointTests(ApiFactory api) => _api = api;

    /// <summary>A signed-in non-admin with their own project, and a provider scoped to it. They are the
    /// project's Maintainer (project creation binds the creator in as one), so they administer the source.</summary>
    private async Task<(HttpClient Client, ProjectDto Project, LlmProviderConfigDto Provider)> SetUpMaintainerAsync(
        string email, string projectKey)
    {
        var admin = await _api.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        var user = await _api.CreateUserAndLoginAsync(admin, "Scope User", email);

        var created = await user.PostAsJsonAsync(
            "/api/projects", new { name = $"Scope {projectKey}", description = (string?)null, key = projectKey });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var project = (await created.Content.ReadFromJsonAsync<ProjectDto>(ApiFactory.Json))!;

        var madeProvider = await user.PostAsJsonAsync(
            $"/api/projects/{project.Id}/llm-providers",
            new { type = "Anthropic", name = "claude", baseUrl = (string?)null, model = "claude-sonnet-4-6", apiKeySecretRef = "KEY" });
        Assert.Equal(HttpStatusCode.OK, madeProvider.StatusCode);
        var provider = (await madeProvider.Content.ReadFromJsonAsync<LlmProviderConfigDto>(ApiFactory.Json))!;

        return (user, project, provider);
    }

    [Fact]
    public async Task MoveToWebsiteWide_IsRefused_ForAProjectMaintainerWhoIsNotASiteAdmin()
    {
        var (user, _, provider) = await SetUpMaintainerAsync("scope-maintainer@test.local", "SCP1");

        var response = await user.PostAsJsonAsync(
            $"/api/llm-providers/{provider.Id}/scope", new { projectId = (Guid?)null });

        // They administer the source, but making something website-wide is site-admin authority.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MoveToAnotherProject_IsRefused_WhenTheCallerDoesNotAdministerTheDestination()
    {
        var (user, _, provider) = await SetUpMaintainerAsync("scope-source@test.local", "SCP2");
        // A project the caller has nothing to do with.
        var admin = await _api.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        var otherCreated = await admin.PostAsJsonAsync(
            "/api/projects", new { name = "Someone Else", description = (string?)null, key = "SCP3" });
        var other = (await otherCreated.Content.ReadFromJsonAsync<ProjectDto>(ApiFactory.Json))!;

        var response = await user.PostAsJsonAsync(
            $"/api/llm-providers/{provider.Id}/scope", new { projectId = other.Id });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MoveToWebsiteWide_Succeeds_ForASiteAdmin()
    {
        var (_, _, provider) = await SetUpMaintainerAsync("scope-admin-src@test.local", "SCP4");
        var admin = await _api.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);

        var response = await admin.PostAsJsonAsync(
            $"/api/llm-providers/{provider.Id}/scope", new { projectId = (Guid?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var moved = (await response.Content.ReadFromJsonAsync<LlmProviderConfigDto>(ApiFactory.Json))!;
        Assert.Null(moved.ProjectId);
    }

    [Fact]
    public async Task ListWebsiteWide_IsRefused_ForANonAdmin()
    {
        // The /providers view now opens on the website-wide list, so this endpoint went from unused to
        // load-bearing. Its site-admin gate is what keeps the route's own guard from being the only one.
        var (user, _, _) = await SetUpMaintainerAsync("scope-lister@test.local", "SCP5");

        var response = await user.GetAsync("/api/llm-providers");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
