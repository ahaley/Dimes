using Dimes.Api;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Lifecycle;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>Covers Epic composition (DIMES-51): <see cref="ChangeRequestService.AddChildAsync"/> and
/// <see cref="ChangeRequestService.RemoveChildAsync"/> compose/break out a child, write the matching
/// audit event, surface children on the Epic's detail, and enforce the structural + role guards.</summary>
public sealed class EpicCompositionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ProjectService _projects;
    private readonly ChangeRequestService _changes;
    private readonly FakeBoardNotifier _notifier = new();

    public EpicCompositionServiceTests()
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

    private async Task<(Guid ProjectId, Guid ActorId)> SeedAsync(MemberRole role = MemberRole.Contributor)
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("Demo", null));
        var member = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Cory", ActorType.Human, "cory@x.com", role));
        return (project.Id, member.ActorId);
    }

    private Task<ChangeRequestDto> NewChangeAsync(Guid projectId, Guid actorId, ChangeKind kind, string title = "Work") =>
        _changes.CreateAsync(projectId, actorId, new CreateChangeRequest(title, null, kind));

    [Fact]
    public async Task AddChild_SetsParent_WritesAudit_AndShowsOnEpicDetail()
    {
        var (projectId, actorId) = await SeedAsync();
        var epic = await NewChangeAsync(projectId, actorId, ChangeKind.Epic, "Epic");
        var child = await NewChangeAsync(projectId, actorId, ChangeKind.Feature, "Child");

        var updated = await _changes.AddChildAsync(epic.Id, actorId, child.Id);

        Assert.Equal(epic.Id, updated.ParentChangeRequestId);
        Assert.Equal(1, await _db.AuditEvents.CountAsync(e => e.Action == "AddedToEpic" && e.EntityId == child.Id));

        var detail = await _changes.GetDetailAsync(epic.Id);
        Assert.Single(detail.Children);
        Assert.Equal(child.Id, detail.Children[0].Id);
    }

    [Fact]
    public async Task RemoveChild_ClearsParent_AndWritesAudit()
    {
        var (projectId, actorId) = await SeedAsync();
        var epic = await NewChangeAsync(projectId, actorId, ChangeKind.Epic, "Epic");
        var child = await NewChangeAsync(projectId, actorId, ChangeKind.Feature, "Child");
        await _changes.AddChildAsync(epic.Id, actorId, child.Id);

        var updated = await _changes.RemoveChildAsync(epic.Id, actorId, child.Id);

        Assert.Null(updated.ParentChangeRequestId);
        Assert.Equal(1, await _db.AuditEvents.CountAsync(e => e.Action == "RemovedFromEpic" && e.EntityId == child.Id));
        Assert.Empty((await _changes.GetDetailAsync(epic.Id)).Children);
    }

    [Fact]
    public async Task AddChild_NonEpicParent_IsRejected()
    {
        var (projectId, actorId) = await SeedAsync();
        var notEpic = await NewChangeAsync(projectId, actorId, ChangeKind.Feature, "Not an epic");
        var child = await NewChangeAsync(projectId, actorId, ChangeKind.Feature, "Child");

        await Assert.ThrowsAsync<BadRequestException>(() => _changes.AddChildAsync(notEpic.Id, actorId, child.Id));
    }

    [Fact]
    public async Task AddChild_EpicAsChild_IsRejected()
    {
        var (projectId, actorId) = await SeedAsync();
        var epic = await NewChangeAsync(projectId, actorId, ChangeKind.Epic, "Epic");
        var otherEpic = await NewChangeAsync(projectId, actorId, ChangeKind.Epic, "Other epic");

        await Assert.ThrowsAsync<BadRequestException>(() => _changes.AddChildAsync(epic.Id, actorId, otherEpic.Id));
    }

    [Fact]
    public async Task AddChild_CrossProject_IsRejected()
    {
        var (projectA, actorA) = await SeedAsync();
        var epic = await NewChangeAsync(projectA, actorA, ChangeKind.Epic, "Epic");

        var projectB = await _projects.CreateAsync(new CreateProjectRequest("Other", null));
        var memberB = await _projects.AddMemberAsync(projectB.Id,
            new AddMemberRequest("Dana", ActorType.Human, "dana@x.com", MemberRole.Contributor));
        var foreignChild = await NewChangeAsync(projectB.Id, memberB.ActorId, ChangeKind.Feature, "Foreign");

        // The actor isn't a member of project B → membership guard trips first; either way it must not compose.
        await Assert.ThrowsAnyAsync<Exception>(() => _changes.AddChildAsync(epic.Id, actorA, foreignChild.Id));
        Assert.Null((await _db.ChangeRequests.FindAsync(foreignChild.Id))!.ParentChangeRequestId);
    }

    [Fact]
    public async Task AddChild_AlreadyComposed_IsRejected()
    {
        var (projectId, actorId) = await SeedAsync();
        var epic1 = await NewChangeAsync(projectId, actorId, ChangeKind.Epic, "Epic 1");
        var epic2 = await NewChangeAsync(projectId, actorId, ChangeKind.Epic, "Epic 2");
        var child = await NewChangeAsync(projectId, actorId, ChangeKind.Feature, "Child");
        await _changes.AddChildAsync(epic1.Id, actorId, child.Id);

        await Assert.ThrowsAsync<BadRequestException>(() => _changes.AddChildAsync(epic2.Id, actorId, child.Id));
    }

    [Fact]
    public async Task AddChild_BelowContributor_IsForbidden()
    {
        var (projectId, actorId) = await SeedAsync(MemberRole.Reporter);
        // A Reporter can't create changes either, so seed the rows directly via a Contributor-less path:
        // build them straight on the context so the role guard is the only thing under test.
        var epic = new Dimes.Domain.Entities.ChangeRequest
        { ProjectId = projectId, Title = "Epic", Kind = ChangeKind.Epic, CreatedByActorId = actorId, Number = 1 };
        var child = new Dimes.Domain.Entities.ChangeRequest
        { ProjectId = projectId, Title = "Child", Kind = ChangeKind.Feature, CreatedByActorId = actorId, Number = 2 };
        _db.ChangeRequests.AddRange(epic, child);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => _changes.AddChildAsync(epic.Id, actorId, child.Id));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
