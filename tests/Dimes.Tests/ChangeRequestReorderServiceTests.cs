using Dimes.Api;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Lifecycle;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>Covers within-column board reordering: <see cref="ChangeRequestService.ReorderAsync"/>
/// assigns sequential SortOrder so <see cref="ChangeRequestService.ListAsync"/> returns the manual
/// order, and rejects an ordered-id set that doesn't match the column exactly.</summary>
public sealed class ChangeRequestReorderServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ProjectService _projects;
    private readonly ChangeRequestService _changes;
    private readonly FakeBoardNotifier _notifier = new();

    public ChangeRequestReorderServiceTests()
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

    private async Task<(Guid ProjectId, Guid ActorId, List<Guid> ChangeIds)> SeedThreeCapturedAsync()
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("Demo", null));
        var member = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Cory", ActorType.Human, "cory@x.com", MemberRole.Contributor));
        var ids = new List<Guid>();
        foreach (var title in new[] { "A", "B", "C" })
        {
            var c = await _changes.CreateAsync(project.Id, member.ActorId,
                new CreateChangeRequest(title, null, ChangeKind.Feature, Priority.None));
            ids.Add(c.Id);
        }
        return (project.Id, member.ActorId, ids);
    }

    [Fact]
    public async Task Reorder_AssignsSequentialSortOrder_AndListReturnsNewOrder()
    {
        var (projectId, actorId, ids) = await SeedThreeCapturedAsync();
        // New order: C, A, B (reverse-ish of the newest-first default).
        var newOrder = new List<Guid> { ids[2], ids[0], ids[1] };

        await _changes.ReorderAsync(projectId, actorId,
            new ReorderChangesRequest(ChangeStatus.Captured, newOrder));

        var listed = await _changes.ListAsync(projectId, ChangeStatus.Captured);
        Assert.Equal(newOrder, listed.Select(c => c.Id).ToList());
        Assert.Equal([1, 2, 3], listed.Select(c => c.SortOrder).ToArray());
        Assert.Contains(_notifier.Events, e => e.Kind == "reordered");
    }

    [Fact]
    public async Task Reorder_IdsNotMatchingColumn_Throws_AndPersistsNothing()
    {
        var (projectId, actorId, ids) = await SeedThreeCapturedAsync();
        // Drop one id → set no longer matches the column exactly.
        var partial = new List<Guid> { ids[0], ids[1] };

        await Assert.ThrowsAsync<BadRequestException>(() => _changes.ReorderAsync(projectId, actorId,
            new ReorderChangesRequest(ChangeStatus.Captured, partial)));

        var listed = await _changes.ListAsync(projectId, ChangeStatus.Captured);
        Assert.All(listed, c => Assert.Equal(0, c.SortOrder)); // untouched
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
