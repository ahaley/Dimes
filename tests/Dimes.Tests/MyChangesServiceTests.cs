using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Lifecycle;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>Covers <see cref="ChangeRequestService.MyOpenChangesAsync"/>, the cross-project "My changes"
/// list: which change requests count as the caller's, and the order they come back in.</summary>
public sealed class MyChangesServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ProjectService _projects;
    private readonly ChangeRequestService _changes;

    public MyChangesServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();

        var resolver = new MembershipResolver(_db);
        _projects = new ProjectService(_db, resolver);
        _changes = new ChangeRequestService(_db, new LifecycleService(), resolver, new FakeBoardNotifier(), new NotificationDispatcher(_db));
    }

    private async Task<Guid> CreateProjectAsync(string name, string? key = null) =>
        (await _projects.CreateAsync(_db, new CreateProjectRequest(name, null, key), Ct)).Id;

    private async Task<Guid> AddMemberAsync(Guid projectId, string name, MemberRole role = MemberRole.Maintainer) =>
        (await _projects.AddMemberAsync(projectId,
            new AddMemberRequest(name, ActorType.Human, $"{name.ToLowerInvariant()}@x.com", role), Ct)).ActorId;

    private async Task<Guid> CaptureAsync(Guid projectId, Guid actorId, string title, Priority priority = Priority.None) =>
        (await _changes.CreateAsync(projectId, actorId,
            new CreateChangeRequest(title, null, ChangeKind.Feature, priority), Ct)).Id;

    private async Task<IReadOnlyList<string>> MyTitlesAsync(Guid actorId) =>
        (await _changes.MyOpenChangesAsync(actorId, Ct)).Select(c => c.Title).ToList();

    [Fact]
    public async Task IncludesChangesAssignedToOrCreatedByTheActorAcrossProjects()
    {
        var alpha = await CreateProjectAsync("Alpha");
        var beta = await CreateProjectAsync("Beta");
        var me = await AddMemberAsync(alpha, "Me");
        var other = await AddMemberAsync(alpha, "Other");
        await _projects.AssignMemberAsync(beta, me, MemberRole.Contributor, Ct);
        await _projects.AssignMemberAsync(beta, other, MemberRole.Contributor, Ct);

        await CaptureAsync(alpha, me, "created in alpha");
        var assigned = await CaptureAsync(beta, other, "assigned in beta");
        await _changes.AssignAsync(assigned, other, new AssignChangeRequest(me), Ct);

        var titles = await MyTitlesAsync(me);

        Assert.Equal(["assigned in beta", "created in alpha"], titles.Order().ToList());
    }

    [Fact]
    public async Task ExcludesChangesTheActorNeitherCreatedNorIsAssigned()
    {
        var project = await CreateProjectAsync("Alpha");
        var me = await AddMemberAsync(project, "Me");
        var other = await AddMemberAsync(project, "Other");
        await CaptureAsync(project, other, "someone else's");

        Assert.Empty(await MyTitlesAsync(me));
    }

    [Fact]
    public async Task ExcludesDoneRejectedAndDuplicate()
    {
        var project = await CreateProjectAsync("Alpha");
        var me = await AddMemberAsync(project, "Me");
        var open = await CaptureAsync(project, me, "open");
        var rejected = await CaptureAsync(project, me, "rejected");
        var duplicate = await CaptureAsync(project, me, "duplicate");
        var done = await CaptureAsync(project, me, "done");

        await _changes.TransitionAsync(rejected, me, new TransitionChangeRequest(ChangeStatus.Rejected, "no", null), Ct);
        await _changes.TransitionAsync(duplicate, me, new TransitionChangeRequest(ChangeStatus.Duplicate, null, open), Ct);
        foreach (var target in new[] { ChangeStatus.Approved, ChangeStatus.InDevelopment, ChangeStatus.InReview, ChangeStatus.Done })
        {
            await _changes.TransitionAsync(done, me, new TransitionChangeRequest(target, null, null), Ct);
        }

        Assert.Equal(["open"], await MyTitlesAsync(me));
    }

    [Fact]
    public async Task ExcludesArchivedProjects()
    {
        var project = await CreateProjectAsync("Alpha");
        var me = await AddMemberAsync(project, "Me");
        await CaptureAsync(project, me, "in archived project");

        await _projects.ArchiveProjectAsync(project, true, me, callerIsSiteAdmin: true, Ct);

        Assert.Empty(await MyTitlesAsync(me));
    }

    [Fact]
    public async Task ExcludesProjectsTheActorIsNoLongerAMemberOf()
    {
        var project = await CreateProjectAsync("Alpha");
        var me = await AddMemberAsync(project, "Me");
        await AddMemberAsync(project, "Other");
        await CaptureAsync(project, me, "created before removal");

        await _projects.RemoveMemberAsync(project, me, Ct);

        Assert.Empty(await MyTitlesAsync(me));
    }

    [Fact]
    public async Task OrdersByPriorityDescendingThenLeastRecentlyUpdated()
    {
        var project = await CreateProjectAsync("Alpha");
        var me = await AddMemberAsync(project, "Me");
        var lowOld = await CaptureAsync(project, me, "low old", Priority.Low);
        var highNew = await CaptureAsync(project, me, "high new", Priority.High);
        var highOld = await CaptureAsync(project, me, "high old", Priority.High);
        await CaptureAsync(project, me, "none", Priority.None);
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, age) in new[] { (lowOld, 10), (highNew, 1), (highOld, 5) })
        {
            var change = await _db.ChangeRequests.SingleAsync(c => c.Id == id, Ct);
            change.UpdatedAt = now.AddDays(-age);
        }
        await _db.SaveChangesAsync(Ct);

        Assert.Equal(["high old", "high new", "low old", "none"], await MyTitlesAsync(me));
    }

    [Fact]
    public async Task PopulatesDisplayKeyFromProjectKey()
    {
        var project = await CreateProjectAsync("Alpha", "ALP");
        var me = await AddMemberAsync(project, "Me");
        await CaptureAsync(project, me, "keyed");

        var change = Assert.Single(await _changes.MyOpenChangesAsync(me, Ct));

        Assert.Equal("ALP-1", change.DisplayKey);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
