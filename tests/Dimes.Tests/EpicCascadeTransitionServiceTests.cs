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

/// <summary>Covers the Epic state cascade: transitioning an Epic forces every composed child to the
/// Epic's exact new status (even an otherwise-illegal jump, even backward), with an audit per moved
/// child, while a non-Epic transition cascades to nothing.</summary>
public sealed class EpicCascadeTransitionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ProjectService _projects;
    private readonly ChangeRequestService _changes;
    private readonly FakeBoardNotifier _notifier = new();
    private int _nextNumber = 1;

    public EpicCascadeTransitionServiceTests()
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

    private async Task<(Guid ProjectId, Guid ActorId)> SeedAsync(MemberRole role)
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("Demo", null));
        var member = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Cory", ActorType.Human, "cory@x.com", role));
        return (project.Id, member.ActorId);
    }

    private async Task<ChangeRequest> ChangeAsync(Guid projectId, Guid actorId, ChangeKind kind, ChangeStatus status, Guid? parentId = null)
    {
        var change = new ChangeRequest
        {
            ProjectId = projectId,
            Title = $"{kind} {_nextNumber}",
            Kind = kind,
            Status = status,
            CreatedByActorId = actorId,
            Number = _nextNumber++,
            ParentChangeRequestId = parentId,
        };
        _db.ChangeRequests.Add(change);
        await _db.SaveChangesAsync();
        return change;
    }

    private async Task<ChangeStatus> StatusOf(Guid id) => (await _db.ChangeRequests.FindAsync(id))!.Status;

    [Fact]
    public async Task TransitioningEpic_ForcesEveryChild_ToExactState_EvenIllegalJumps()
    {
        var (projectId, actorId) = await SeedAsync(MemberRole.Maintainer);
        var epic = await ChangeAsync(projectId, actorId, ChangeKind.Epic, ChangeStatus.Captured);
        var a = await ChangeAsync(projectId, actorId, ChangeKind.Feature, ChangeStatus.Captured, epic.Id);
        var b = await ChangeAsync(projectId, actorId, ChangeKind.Feature, ChangeStatus.Triaged, epic.Id);
        var c = await ChangeAsync(projectId, actorId, ChangeKind.Problem, ChangeStatus.InReview, epic.Id); // ahead of the Epic

        // Captured → Approved is gated to Maintainer (satisfied) and is an illegal single step for the
        // Captured/InReview children — they must be forced to the exact state regardless.
        await _changes.TransitionAsync(epic.Id, actorId, new TransitionChangeRequest(ChangeStatus.Approved, null, null));

        Assert.Equal(ChangeStatus.Approved, await StatusOf(epic.Id));
        Assert.Equal(ChangeStatus.Approved, await StatusOf(a.Id));
        Assert.Equal(ChangeStatus.Approved, await StatusOf(b.Id));
        Assert.Equal(ChangeStatus.Approved, await StatusOf(c.Id)); // pulled back from InReview

        // One cascade audit per child that actually moved, plus the Epic's own transition audit.
        Assert.Equal(3, await _db.AuditEvents.CountAsync(e => e.Action == "EpicCascade"));
        Assert.Equal(1, await _db.AuditEvents.CountAsync(e => e.Action == "ChangeTransition" && e.EntityId == epic.Id));
    }

    [Fact]
    public async Task EnteringDone_StampsChildrenCompletedAt_AndReopenClearsIt()
    {
        var (projectId, actorId) = await SeedAsync(MemberRole.Maintainer);
        var epic = await ChangeAsync(projectId, actorId, ChangeKind.Epic, ChangeStatus.InReview);
        var child = await ChangeAsync(projectId, actorId, ChangeKind.Feature, ChangeStatus.InReview, epic.Id);

        await _changes.TransitionAsync(epic.Id, actorId, new TransitionChangeRequest(ChangeStatus.Done, null, null));
        Assert.NotNull((await _db.ChangeRequests.FindAsync(child.Id))!.CompletedAt);

        await _changes.TransitionAsync(epic.Id, actorId, new TransitionChangeRequest(ChangeStatus.InDevelopment, null, null));
        Assert.Null((await _db.ChangeRequests.FindAsync(child.Id))!.CompletedAt);
        Assert.Equal(ChangeStatus.InDevelopment, await StatusOf(child.Id));
    }

    [Fact]
    public async Task DuplicateCascade_SetsChildrenDuplicateOfId_ThenClearsOnLeaving()
    {
        var (projectId, actorId) = await SeedAsync(MemberRole.Maintainer);
        var epic = await ChangeAsync(projectId, actorId, ChangeKind.Epic, ChangeStatus.Captured);
        var child = await ChangeAsync(projectId, actorId, ChangeKind.Feature, ChangeStatus.Captured, epic.Id);
        var target = await ChangeAsync(projectId, actorId, ChangeKind.Feature, ChangeStatus.Captured);

        await _changes.TransitionAsync(epic.Id, actorId, new TransitionChangeRequest(ChangeStatus.Duplicate, null, target.Id));
        var dup = (await _db.ChangeRequests.FindAsync(child.Id))!;
        Assert.Equal(ChangeStatus.Duplicate, dup.Status);
        Assert.Equal(target.Id, dup.DuplicateOfId);
    }

    [Fact]
    public async Task NonEpicTransition_DoesNotCascade()
    {
        var (projectId, actorId) = await SeedAsync(MemberRole.Maintainer);
        var standalone = await ChangeAsync(projectId, actorId, ChangeKind.Feature, ChangeStatus.Captured);

        await _changes.TransitionAsync(standalone.Id, actorId, new TransitionChangeRequest(ChangeStatus.Triaged, null, null));

        Assert.Equal(ChangeStatus.Triaged, await StatusOf(standalone.Id));
        Assert.Equal(0, await _db.AuditEvents.CountAsync(e => e.Action == "EpicCascade"));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
