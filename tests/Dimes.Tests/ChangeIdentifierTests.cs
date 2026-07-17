using Dimes.Api;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Domain.Lifecycle;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>Covers the human-readable identifier scheme: project keys (validated/unique, or derived) and
/// per-project change numbers forming the "KEY-NUMBER" display id, plus the startup backfill.</summary>
public sealed class ChangeIdentifierTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ProjectService _projects;
    private readonly ChangeRequestService _changes;
    private readonly FakeBoardNotifier _notifier = new();

    public ChangeIdentifierTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();

        var resolver = new MembershipResolver(_db);
        _projects = new ProjectService(_db, resolver);
        _changes = new ChangeRequestService(_db, new LifecycleService(), resolver, _notifier, new NotificationDispatcher(_db));
    }

    private async Task<(Guid ProjectId, Guid ActorId)> SeedProjectAsync(string name, string? key)
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest(name, null, key));
        // No email: this helper seeds several projects, and email is now a unique login identity, so a
        // shared address would (correctly) collide. These tests don't exercise login.
        var member = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Cory", ActorType.Human, null, MemberRole.Contributor));
        return (project.Id, member.ActorId);
    }

    // ----- Project keys -----

    [Fact]
    public async Task CreateProject_StoresKeyUppercased()
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("Acme Web", null, "acme"));
        Assert.Equal("ACME", project.Key);
    }

    [Theory]
    [InlineData("A")]          // too short
    [InlineData("TOOLONGKEY")] // too long
    [InlineData("1AB")]        // must start with a letter
    [InlineData("A-B")]        // invalid char
    public async Task CreateProject_RejectsBadKeyFormat(string key)
    {
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _projects.CreateAsync(new CreateProjectRequest("P", null, key)));
    }

    [Fact]
    public async Task CreateProject_RejectsDuplicateKey()
    {
        await _projects.CreateAsync(new CreateProjectRequest("First", null, "DIMES"));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _projects.CreateAsync(new CreateProjectRequest("Second", null, "dimes")));
    }

    [Fact]
    public async Task CreateProject_DerivesKeyWhenOmitted()
    {
        var a = await _projects.CreateAsync(new CreateProjectRequest("Mobile App", null, null));
        var b = await _projects.CreateAsync(new CreateProjectRequest("Mobile App", null, null)); // same name → unique key
        Assert.True(ProjectKeys.IsValid(a.Key!));
        Assert.True(ProjectKeys.IsValid(b.Key!));
        Assert.NotEqual(a.Key, b.Key);
    }

    // ----- Per-project change numbers -----

    [Fact]
    public async Task CreateChange_NumbersSequentiallyPerProject_WithDisplayKey()
    {
        var (projectId, actorId) = await SeedProjectAsync("Dimes", "DIMES");

        var c1 = await _changes.CreateAsync(projectId, actorId, new CreateChangeRequest("One", null, ChangeKind.Feature, Priority.None));
        var c2 = await _changes.CreateAsync(projectId, actorId, new CreateChangeRequest("Two", null, ChangeKind.Feature, Priority.None));

        Assert.Equal(1, c1.Number);
        Assert.Equal(2, c2.Number);
        Assert.Equal("DIMES-1", c1.DisplayKey);
        Assert.Equal("DIMES-2", c2.DisplayKey);
    }

    [Fact]
    public async Task ChangeNumbers_AreIndependentPerProject()
    {
        var (p1, a1) = await SeedProjectAsync("Alpha", "ALPHA");
        var (p2, a2) = await SeedProjectAsync("Beta", "BETA");

        var first1 = await _changes.CreateAsync(p1, a1, new CreateChangeRequest("x", null, ChangeKind.Feature, Priority.None));
        var first2 = await _changes.CreateAsync(p2, a2, new CreateChangeRequest("y", null, ChangeKind.Feature, Priority.None));

        Assert.Equal(1, first1.Number); // each project starts at 1
        Assert.Equal(1, first2.Number);
        Assert.Equal("ALPHA-1", first1.DisplayKey);
        Assert.Equal("BETA-1", first2.DisplayKey);
    }

    [Fact]
    public async Task CreateMany_AssignsContiguousBlock()
    {
        var (projectId, actorId) = await SeedProjectAsync("Dimes", "DIMES");
        await _changes.CreateAsync(projectId, actorId, new CreateChangeRequest("seed", null, ChangeKind.Feature, Priority.None));

        var created = await _changes.CreateManyAsync(projectId, actorId,
        [
            new CreateChangeRequest("a", null, ChangeKind.Feature, Priority.None),
            new CreateChangeRequest("b", null, ChangeKind.Feature, Priority.None),
        ]);

        Assert.Equal([2, 3], created.Select(c => c.Number).ToArray());
        Assert.Equal(["DIMES-2", "DIMES-3"], created.Select(c => c.DisplayKey!).ToArray());
    }

    // ----- Startup backfill -----

    [Fact]
    public async Task Backfill_AssignsKeyAndNumbers_AndIsIdempotent()
    {
        // Simulate legacy rows (pre-feature): a project with no key and changes with no numbers.
        var project = new Project { Name = "Legacy Web" };
        var actor = new Actor { DisplayName = "Dev", Type = ActorType.Human };
        _db.Projects.Add(project);
        _db.Actors.Add(actor);
        var older = new ChangeRequest
        {
            Project = project, Title = "older", Kind = ChangeKind.Feature, Status = ChangeStatus.Captured,
            CreatedBy = actor, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-2),
        };
        var newer = new ChangeRequest
        {
            Project = project, Title = "newer", Kind = ChangeKind.Feature, Status = ChangeStatus.Captured,
            CreatedBy = actor, CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        };
        _db.ChangeRequests.AddRange(older, newer);
        await _db.SaveChangesAsync();
        Assert.Null(project.Key);
        Assert.Null(older.Number);

        await new IdentifierBootstrapper(_db).BackfillAsync();

        Assert.NotNull(project.Key);
        Assert.True(ProjectKeys.IsValid(project.Key!));
        Assert.Equal(1, older.Number); // numbered by CreatedAt order
        Assert.Equal(2, newer.Number);

        // Idempotent: a second run changes nothing.
        var key = project.Key;
        await new IdentifierBootstrapper(_db).BackfillAsync();
        Assert.Equal(key, project.Key);
        Assert.Equal(1, older.Number);
        Assert.Equal(2, newer.Number);
    }

    [Fact]
    public async Task Backfill_StampsCompletedAt_OnPreFeatureDoneChanges_AndIsIdempotent()
    {
        var project = new Project { Name = "Legacy", Key = "LEG" };
        var actor = new Actor { DisplayName = "Dev", Type = ActorType.Human };
        _db.Projects.Add(project);
        _db.Actors.Add(actor);
        var updatedAt = DateTimeOffset.UtcNow.AddDays(-30);
        var done = new ChangeRequest
        {
            Project = project, Title = "shipped", Kind = ChangeKind.Feature, Status = ChangeStatus.Done,
            CreatedBy = actor, Number = 1, UpdatedAt = updatedAt, CompletedAt = null,
        };
        var open = new ChangeRequest
        {
            Project = project, Title = "wip", Kind = ChangeKind.Feature, Status = ChangeStatus.InDevelopment,
            CreatedBy = actor, Number = 2, CompletedAt = null,
        };
        _db.ChangeRequests.AddRange(done, open);
        await _db.SaveChangesAsync();

        await new IdentifierBootstrapper(_db).BackfillAsync();

        Assert.Equal(updatedAt, done.CompletedAt); // Done backfilled from UpdatedAt
        Assert.Null(open.CompletedAt);              // non-Done untouched

        // Idempotent: a second run leaves the stamped value alone.
        await new IdentifierBootstrapper(_db).BackfillAsync();
        Assert.Equal(updatedAt, done.CompletedAt);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
