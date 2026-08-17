using Dimes.Api;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>Moving an LLM provider between scopes. The interesting behaviour is the refusal: an Agent actor
/// references a provider by id and nothing re-checks that assignment when the provider itself moves, so
/// narrowing a provider's scope could otherwise leave an agent pointing at something its project can no
/// longer see.</summary>
public sealed class LlmProviderScopeTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ProjectService _projects;

    public LlmProviderScopeTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();
        _projects = new ProjectService(_db, new MembershipResolver(_db));
    }

    private async Task<LlmProviderConfigDto> CreateProviderAsync(Guid? projectId) =>
        await _projects.CreateLlmProviderAsync(projectId,
            new CreateLlmProviderRequest(LlmProviderType.Anthropic, "claude", null, "claude-sonnet-4-6", "KEY"));

    /// <summary>An Agent actor that uses the provider and is a member of <paramref name="projectId"/>.</summary>
    private async Task<Actor> CreateAgentAsync(Guid providerId, Guid projectId)
    {
        var agent = new Actor
        {
            DisplayName = "Aria",
            Type = ActorType.Agent,
            LlmProviderConfigId = providerId,
        };
        _db.Actors.Add(agent);
        _db.Memberships.Add(new Membership { ActorId = agent.Id, ProjectId = projectId, Role = MemberRole.Contributor });
        await _db.SaveChangesAsync();
        return agent;
    }

    [Fact]
    public async Task MoveScope_ProjectToWebsiteWide_AndBack()
    {
        var project = await _projects.CreateAsync(_db, new CreateProjectRequest("P", null));
        var created = await CreateProviderAsync(project.Id);
        Assert.Equal(project.Id, created.ProjectId);

        var promoted = await _projects.MoveLlmProviderScopeAsync(created.Id, null);
        Assert.Null(promoted.ProjectId);

        var demoted = await _projects.MoveLlmProviderScopeAsync(created.Id, project.Id);
        Assert.Equal(project.Id, demoted.ProjectId);
    }

    [Fact]
    public async Task MoveScope_BetweenProjects()
    {
        var a = await _projects.CreateAsync(_db, new CreateProjectRequest("A", null));
        var b = await _projects.CreateAsync(_db, new CreateProjectRequest("B", null));
        var created = await CreateProviderAsync(a.Id);

        var moved = await _projects.MoveLlmProviderScopeAsync(created.Id, b.Id);

        Assert.Equal(b.Id, moved.ProjectId);
        // The project list is what feeds the agent picker, so confirm the move actually changed visibility.
        Assert.DoesNotContain(created.Id, (await _projects.ListLlmProvidersAsync(a.Id)).Select(p => p.Id));
        Assert.Contains(created.Id, (await _projects.ListLlmProvidersAsync(b.Id)).Select(p => p.Id));
    }

    [Fact]
    public async Task MoveScope_UnchangedScope_IsANoOp()
    {
        var project = await _projects.CreateAsync(_db, new CreateProjectRequest("P", null));
        var created = await CreateProviderAsync(project.Id);

        var same = await _projects.MoveLlmProviderScopeAsync(created.Id, project.Id);

        Assert.Equal(project.Id, same.ProjectId);
    }

    [Fact]
    public async Task MoveScope_UnknownDestinationProject_IsNotFound()
    {
        var created = await CreateProviderAsync(null);

        await Assert.ThrowsAsync<NotFoundException>(
            () => _projects.MoveLlmProviderScopeAsync(created.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task MoveScope_UnknownProvider_IsNotFound()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _projects.MoveLlmProviderScopeAsync(Guid.NewGuid(), null));
    }

    [Fact]
    public async Task MoveScope_ToWebsiteWide_IsAllowedEvenWhileAgentsUseIt()
    {
        // Widening never strands anyone: a website-wide provider is available to every project.
        var project = await _projects.CreateAsync(_db, new CreateProjectRequest("P", null));
        var created = await CreateProviderAsync(project.Id);
        await CreateAgentAsync(created.Id, project.Id);

        var promoted = await _projects.MoveLlmProviderScopeAsync(created.Id, null);

        Assert.Null(promoted.ProjectId);
    }

    [Fact]
    public async Task MoveScope_NarrowingAwayFromAnAgentsProject_IsRefused()
    {
        var a = await _projects.CreateAsync(_db, new CreateProjectRequest("A", null));
        var b = await _projects.CreateAsync(_db, new CreateProjectRequest("B", null));
        var created = await CreateProviderAsync(null); // website-wide, so the agent in A can use it
        var agent = await CreateAgentAsync(created.Id, a.Id);

        // Scoping it to B alone would leave the agent in A holding a provider A can't see.
        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _projects.MoveLlmProviderScopeAsync(created.Id, b.Id));
        Assert.Contains("Reassign", ex.Message);

        // Still website-wide — the refusal must not have half-applied.
        Assert.Null((await _projects.ListGlobalLlmProvidersAsync()).Single(p => p.Id == created.Id).ProjectId);

        // Once that agent is also a member of B, nobody is stranded and the move goes through.
        _db.Memberships.Add(new Membership { ActorId = agent.Id, ProjectId = b.Id, Role = MemberRole.Contributor });
        await _db.SaveChangesAsync();

        var moved = await _projects.MoveLlmProviderScopeAsync(created.Id, b.Id);
        Assert.Equal(b.Id, moved.ProjectId);
    }

    [Fact]
    public async Task MoveScope_NarrowingWithinTheAgentsOwnProject_IsAllowed()
    {
        var project = await _projects.CreateAsync(_db, new CreateProjectRequest("P", null));
        var created = await CreateProviderAsync(null);
        await CreateAgentAsync(created.Id, project.Id);

        var moved = await _projects.MoveLlmProviderScopeAsync(created.Id, project.Id);

        Assert.Equal(project.Id, moved.ProjectId);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
