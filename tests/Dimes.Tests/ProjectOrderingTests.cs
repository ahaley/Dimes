using Dimes.Api;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>Covers per-user project ordering: <see cref="ProjectService.ReorderProjectsAsync"/> persists
/// an actor's personal order and <see cref="ProjectService.ListAsync"/> returns projects in it (unranked
/// projects falling back to alphabetical), independently per actor.</summary>
public sealed class ProjectOrderingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ProjectService _projects;

    public ProjectOrderingTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();
        _projects = new ProjectService(_db, new MembershipResolver(_db));
    }

    private async Task<Guid> SeedSiteAdminAsync(string name)
    {
        var actor = new Actor { DisplayName = name, Type = ActorType.Human, IsSiteAdmin = true };
        _db.Actors.Add(actor);
        await _db.SaveChangesAsync();
        return actor.Id;
    }

    [Fact]
    public async Task Reorder_OrdersByPersonalOrder_UnrankedFallBackToName()
    {
        var actorId = await SeedSiteAdminAsync("Admin");
        var alpha = await _projects.CreateAsync(new CreateProjectRequest("Alpha", null, "ALPHA"));
        var beta = await _projects.CreateAsync(new CreateProjectRequest("Beta", null, "BETA"));
        var gamma = await _projects.CreateAsync(new CreateProjectRequest("Gamma", null, "GAMMA"));

        // Personal order ranks gamma then alpha; beta is left unranked.
        await _projects.ReorderProjectsAsync(actorId, new ReorderProjectsRequest([gamma.Id, alpha.Id]));

        var listed = await _projects.ListAsync(actorId, isSiteAdmin: true);
        Assert.Equal([gamma.Id, alpha.Id, beta.Id], listed.Select(p => p.Id).ToArray()); // ranked first, beta (unranked) last by name
    }

    [Fact]
    public async Task ProjectOrder_IsPerActor()
    {
        var actor1 = await SeedSiteAdminAsync("One");
        var actor2 = await SeedSiteAdminAsync("Two");
        var a = await _projects.CreateAsync(new CreateProjectRequest("Apple", null, "APP"));
        var b = await _projects.CreateAsync(new CreateProjectRequest("Box", null, "BOX"));
        var c = await _projects.CreateAsync(new CreateProjectRequest("Cat", null, "CAT"));

        await _projects.ReorderProjectsAsync(actor1, new ReorderProjectsRequest([c.Id, b.Id, a.Id]));

        Assert.Equal([c.Id, b.Id, a.Id], (await _projects.ListAsync(actor1, true)).Select(p => p.Id).ToArray());
        // actor2 has no personal order → default alphabetical, unaffected by actor1's reorder.
        Assert.Equal([a.Id, b.Id, c.Id], (await _projects.ListAsync(actor2, true)).Select(p => p.Id).ToArray());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
