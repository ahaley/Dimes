using Dimes.Api;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Lifecycle;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>Covers the Freestyle-Mode confirm step: <see cref="ChangeRequestService.CreateManyAsync"/>
/// creates a whole batch in one transaction (all Captured, each audited), and rejects an empty batch or
/// any blank title before writing anything.</summary>
public sealed class ChangeRequestBatchServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ProjectService _projects;
    private readonly ChangeRequestService _changes;
    private readonly FakeBoardNotifier _notifier = new();

    public ChangeRequestBatchServiceTests()
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

    private async Task<(Guid ProjectId, Guid ActorId)> SeedAsync()
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("Demo", null));
        var member = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Cory", ActorType.Human, "cory@x.com", MemberRole.Contributor));
        return (project.Id, member.ActorId);
    }

    [Fact]
    public async Task CreateMany_CreatesAllInCaptured_WithAuditAndNotifications()
    {
        var (projectId, actorId) = await SeedAsync();

        var created = await _changes.CreateManyAsync(projectId, actorId,
        [
            new CreateChangeRequest("  Add CSV export ", "desc", ChangeKind.Feature, Priority.High),
            new CreateChangeRequest("Fix slow inbox", null, ChangeKind.Problem, Priority.Medium),
        ]);

        Assert.Equal(2, created.Count);
        Assert.All(created, c => Assert.Equal(ChangeStatus.Captured, c.Status));
        Assert.Equal("Add CSV export", created[0].Title); // trimmed
        Assert.Equal(2, await _db.ChangeRequests.CountAsync());
        Assert.Equal(2, await _db.AuditEvents.CountAsync(e => e.Action == "Created"));
        Assert.Equal(2, _notifier.Events.Count);
    }

    [Fact]
    public async Task CreateMany_EmptyBatch_IsRejected()
    {
        var (projectId, actorId) = await SeedAsync();
        await Assert.ThrowsAsync<BadRequestException>(() => _changes.CreateManyAsync(projectId, actorId, []));
    }

    [Fact]
    public async Task CreateMany_AnyBlankTitle_RejectsWholeBatch_AndWritesNothing()
    {
        var (projectId, actorId) = await SeedAsync();

        await Assert.ThrowsAsync<BadRequestException>(() => _changes.CreateManyAsync(projectId, actorId,
        [
            new CreateChangeRequest("Valid", null, ChangeKind.Feature, Priority.None),
            new CreateChangeRequest("   ", null, ChangeKind.Feature, Priority.None),
        ]));

        Assert.Equal(0, await _db.ChangeRequests.CountAsync()); // atomic: nothing persisted
    }

    [Fact]
    public async Task CreateMany_WithObservationDrivenKind_RejectsWholeBatch_AndWritesNothing()
    {
        var (projectId, actorId) = await SeedAsync();

        await Assert.ThrowsAsync<BadRequestException>(() => _changes.CreateManyAsync(projectId, actorId,
        [
            new CreateChangeRequest("Valid", null, ChangeKind.Feature, Priority.None),
            new CreateChangeRequest("Sneaky", null, ChangeKind.ObservationDriven, Priority.None),
        ]));

        Assert.Equal(0, await _db.ChangeRequests.CountAsync()); // manual ObservationDriven is provenance-only
    }

    [Fact]
    public async Task Create_WithObservationDrivenKind_IsRejected()
    {
        var (projectId, actorId) = await SeedAsync();

        await Assert.ThrowsAsync<BadRequestException>(() => _changes.CreateAsync(projectId, actorId,
            new CreateChangeRequest("Manual signal", null, ChangeKind.ObservationDriven, Priority.None)));

        Assert.Equal(0, await _db.ChangeRequests.CountAsync());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
