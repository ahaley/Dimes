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

/// <summary>Covers best-effort bulk transition of an Epic + its children (DIMES-52): legal/authorized
/// members move and are audited; members for which the move is illegal from their current status or for
/// which the actor lacks the role are skipped and reported, not failed.</summary>
public sealed class EpicBulkTransitionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ProjectService _projects;
    private readonly ChangeRequestService _changes;
    private readonly FakeBoardNotifier _notifier = new();
    private int _nextNumber = 1;

    public EpicBulkTransitionServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();

        var resolver = new MembershipResolver(_db);
        _projects = new ProjectService(_db, resolver);
        _changes = new ChangeRequestService(_db, new LifecycleService(), resolver, _notifier);
    }

    private async Task<(Guid ProjectId, Guid ActorId)> SeedAsync(MemberRole role)
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("Demo", null));
        var member = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Cory", ActorType.Human, "cory@x.com", role));
        return (project.Id, member.ActorId);
    }

    // Persist a change with an exact status (and optional parent), bypassing the lifecycle so a test can
    // set up the mixed states bulk transition must cope with.
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

    [Fact]
    public async Task Bulk_BestEffort_MovesLegalMembers_SkipsIllegal_AndAuditsEachMove()
    {
        var (projectId, actorId) = await SeedAsync(MemberRole.Maintainer);
        var epic = await ChangeAsync(projectId, actorId, ChangeKind.Epic, ChangeStatus.Captured);
        var childApproved = await ChangeAsync(projectId, actorId, ChangeKind.Feature, ChangeStatus.Approved, epic.Id);
        var childInDev = await ChangeAsync(projectId, actorId, ChangeKind.Feature, ChangeStatus.InDevelopment, epic.Id);

        // Target Approved: epic (Captured→Approved) and childInDev (InDevelopment→Approved) are legal for a
        // Maintainer; childApproved is already Approved → illegal (from == to) → skipped.
        var result = await _changes.BulkTransitionAsync(epic.Id, actorId, ChangeStatus.Approved, "ship it");

        Assert.Contains(epic.Id, result.Transitioned);
        Assert.Contains(childInDev.Id, result.Transitioned);
        Assert.Equal(2, result.Transitioned.Count);
        Assert.Single(result.Skipped);
        Assert.Equal(childApproved.Id, result.Skipped[0].Id);
        Assert.False(string.IsNullOrWhiteSpace(result.Skipped[0].Reason));

        // One ChangeTransition audit per member that actually moved.
        Assert.Equal(2, await _db.AuditEvents.CountAsync(e => e.Action == "ChangeTransition"));
        Assert.Equal(ChangeStatus.Approved, (await _db.ChangeRequests.FindAsync(epic.Id))!.Status);
        Assert.Equal(ChangeStatus.Approved, (await _db.ChangeRequests.FindAsync(childInDev.Id))!.Status);
        Assert.Equal(ChangeStatus.Approved, (await _db.ChangeRequests.FindAsync(childApproved.Id))!.Status); // unchanged
    }

    [Fact]
    public async Task Bulk_SkipsMembers_WhenActorLacksRole_AndWritesNothing()
    {
        var (projectId, actorId) = await SeedAsync(MemberRole.Contributor);
        var epic = await ChangeAsync(projectId, actorId, ChangeKind.Epic, ChangeStatus.InReview);
        var child = await ChangeAsync(projectId, actorId, ChangeKind.Feature, ChangeStatus.InReview, epic.Id);

        // → Done requires Maintainer; a Contributor's whole batch is skipped (role guard), nothing persists.
        var result = await _changes.BulkTransitionAsync(epic.Id, actorId, ChangeStatus.Done, null);

        Assert.Empty(result.Transitioned);
        Assert.Equal(2, result.Skipped.Count);
        Assert.Equal(0, await _db.AuditEvents.CountAsync(e => e.Action == "ChangeTransition"));
        Assert.Equal(ChangeStatus.InReview, (await _db.ChangeRequests.FindAsync(epic.Id))!.Status);
        Assert.Equal(ChangeStatus.InReview, (await _db.ChangeRequests.FindAsync(child.Id))!.Status);
    }

    [Fact]
    public async Task Bulk_OnNonEpic_IsRejected()
    {
        var (projectId, actorId) = await SeedAsync(MemberRole.Maintainer);
        var notEpic = await ChangeAsync(projectId, actorId, ChangeKind.Feature, ChangeStatus.Captured);

        await Assert.ThrowsAsync<BadRequestException>(
            () => _changes.BulkTransitionAsync(notEpic.Id, actorId, ChangeStatus.Triaged, null));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
