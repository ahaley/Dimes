using System.Net;
using System.Net.Http.Json;
using Dimes.Api.Contracts;
using Dimes.Domain;

namespace Dimes.Tests;

/// <summary>Endpoint-level cover for project creation, which opening it to non-admins made necessary: the
/// change removed <c>[Authorize(SiteAdminPolicy)]</c> from <c>ProjectsController.Create</c>, leaving the
/// endpoint's protection entirely outside the service layer. No service-level test can see it —
/// <c>ProjectService</c> is handed an actor id and never learns whether the request was authenticated at
/// all — so these drive the real pipeline instead.
///
/// Anonymous access is refused by two independent layers, verified by deleting each in turn: the global
/// fallback policy, and <c>ICurrentActor.ActorId</c> throwing <c>UnauthorizedException</c> when the
/// session carries no actor claim. Removing either alone still yields 401, which is why these tests
/// assert the observable outcome rather than naming a mechanism.</summary>
public sealed class ProjectCreationEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _api;

    public ProjectCreationEndpointTests(ApiFactory api) => _api = api;

    private static object NewProject(string name, string key) => new { name, description = (string?)null, key };

    /// <summary>The regression this whole file exists for. With the site-admin attribute gone, an
    /// unauthenticated POST must still be refused — by the fallback policy, which is easy to weaken
    /// accidentally and invisible to every other test.</summary>
    [Fact]
    public async Task Create_IsRefused_WhenUnauthenticated()
    {
        var anonymous = _api.CreateSessionClient();

        var response = await anonymous.PostAsJsonAsync("/api/projects", NewProject("Sneaky", "SNEAK"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Quota_IsRefused_WhenUnauthenticated()
    {
        var anonymous = _api.CreateSessionClient();

        var response = await anonymous.GetAsync("/api/projects/quota");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>The point of the change: an ordinary signed-in user creates a project and lands as its
    /// Maintainer. Before this, the endpoint answered 403 for anyone but a site admin.</summary>
    [Fact]
    public async Task Create_Succeeds_ForAnAuthenticatedNonAdmin()
    {
        var admin = await _api.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        var user = await _api.CreateUserAndLoginAsync(admin, "Endpoint User", "endpoint-user@test.local");

        var response = await user.PostAsJsonAsync("/api/projects", NewProject("User Project", "EPU1"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var project = await response.Content.ReadFromJsonAsync<ProjectDto>(ApiFactory.Json);
        Assert.Equal(MemberRole.Maintainer, project!.MyRole);
    }

    /// <summary>The quota is enforced through the pipeline, not only in the service: a user past their
    /// allowance gets a 403 whose detail explains the limit (ForbiddenException → ProblemDetails).</summary>
    [Fact]
    public async Task Create_Is403_OnceTheAllowanceIsSpent()
    {
        var admin = await _api.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        var user = await _api.CreateUserAndLoginAsync(admin, "Capped User", "capped-user@test.local");

        var quota = await (await user.GetAsync("/api/projects/quota")).Content.ReadFromJsonAsync<ProjectQuotaDto>(ApiFactory.Json);
        for (var i = 0; i < quota!.Limit; i++)
        {
            var ok = await user.PostAsJsonAsync("/api/projects", NewProject($"Capped {i}", $"CAP{i}"));
            Assert.Equal(HttpStatusCode.Created, ok.StatusCode);
        }

        var refused = await user.PostAsJsonAsync("/api/projects", NewProject("One too many", "CAPX"));

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        var problem = await refused.Content.ReadFromJsonAsync<ProblemDetailsBody>(ApiFactory.Json);
        Assert.Contains("allowed projects", problem!.Detail);
    }

    /// <summary>Site administration stays admin-only. Opening project creation must not have loosened the
    /// endpoints that set the limits — otherwise a capped user could simply raise their own.</summary>
    [Theory]
    [InlineData("/api/admin/project-policy")]
    [InlineData("/api/admin/users")]
    public async Task AdminEndpoints_StayForbidden_ForANonAdmin(string path)
    {
        var admin = await _api.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        var user = await _api.CreateUserAndLoginAsync(
            admin, $"Prober {path.GetHashCode():X}", $"prober-{Math.Abs(path.GetHashCode()):X}@test.local");

        var response = await user.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>A capped user must not be able to lift their own cap.</summary>
    [Fact]
    public async Task SettingAProjectLimit_IsRefused_ForANonAdmin()
    {
        var admin = await _api.LoginAsync(ApiFactory.AdminEmail, ApiFactory.AdminPassword);
        var user = await _api.CreateUserAndLoginAsync(admin, "Self Raiser", "self-raiser@test.local");
        var me = await (await user.GetAsync("/api/auth/me")).Content.ReadFromJsonAsync<MeDto>(ApiFactory.Json);

        var response = await user.PutAsJsonAsync($"/api/admin/users/{me!.ActorId}/project-limit", new { limit = 99 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Only the fields the assertions need; ProblemDetails is serialized by the framework.</summary>
    private sealed record ProblemDetailsBody(string? Title, string? Detail, int? Status);
}
