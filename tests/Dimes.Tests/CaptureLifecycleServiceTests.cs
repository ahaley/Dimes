using Dimes.Api;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Lifecycle;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>
/// Drives the capture → inbox → promote → lifecycle loop through the application services on an
/// in-memory SQLite database, covering fingerprint aggregation, promotion-with-evidence, RBAC
/// enforcement, and the full happy path with its audit trail.
/// </summary>
public sealed class CaptureLifecycleServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ProjectService _projects;
    private readonly ObservationService _observations;
    private readonly ChangeRequestService _changes;

    public CaptureLifecycleServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();

        var lifecycle = new LifecycleService();
        var resolver = new MembershipResolver(_db);
        _projects = new ProjectService(_db);
        _observations = new ObservationService(_db, lifecycle, resolver);
        _changes = new ChangeRequestService(_db, lifecycle, resolver);
    }

    private async Task<(Guid ProjectId, Guid MaintainerId, Guid ContributorId, Guid SourceId)> SeedAsync()
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("Demo", null));
        var maintainer = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Maud", ActorType.Human, "maud@x.com", MemberRole.Maintainer));
        var contributor = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Cory", ActorType.Human, "cory@x.com", MemberRole.Contributor));
        var source = await _observations.CreateSourceAsync(project.Id,
            new CreateSourceRequest(ObservationSourceType.Sdk, "web-sdk", null));
        return (project.Id, maintainer.ActorId, contributor.ActorId, source.Id);
    }

    [Fact]
    public async Task ListMembers_ProjectsActorNavigation()
    {
        var seed = await SeedAsync();

        var members = await _projects.ListMembersAsync(seed.ProjectId);

        Assert.Equal(2, members.Count);
        Assert.Contains(members, m => m.DisplayName == "Maud" && m.Role == MemberRole.Maintainer);
        Assert.Contains(members, m => m.DisplayName == "Cory" && m.Role == MemberRole.Contributor);
    }

    [Fact]
    public async Task Ingest_SameFingerprint_AggregatesIntoOneObservation()
    {
        var seed = await SeedAsync();
        var req = new IngestObservationRequest(ObservationKind.TechnicalError, "{\"err\":\"boom\"}", null, "sig-1");

        var first = await _observations.IngestAsync(seed.SourceId, req);
        var second = await _observations.IngestAsync(seed.SourceId, req);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(2, second.OccurrenceCount);

        var inbox = await _observations.ListInboxAsync(seed.ProjectId, ObservationStatus.New);
        Assert.Single(inbox);
    }

    [Fact]
    public async Task Ingest_DifferentFingerprints_CreatesDistinctObservations()
    {
        var seed = await SeedAsync();
        await _observations.IngestAsync(seed.SourceId,
            new IngestObservationRequest(ObservationKind.TechnicalError, "{}", null, "sig-a"));
        await _observations.IngestAsync(seed.SourceId,
            new IngestObservationRequest(ObservationKind.TechnicalError, "{}", null, "sig-b"));

        var inbox = await _observations.ListInboxAsync(seed.ProjectId, null);
        Assert.Equal(2, inbox.Count);
    }

    [Fact]
    public async Task Promote_CreatesObservationDrivenChange_WithEvidence()
    {
        var seed = await SeedAsync();
        var obs = await _observations.IngestAsync(seed.SourceId,
            new IngestObservationRequest(ObservationKind.ExplicitFeedback, "{\"msg\":\"add export\"}", null, null));

        var change = await _observations.PromoteAsync(obs.Id,
            new PromoteObservationRequest(seed.ContributorId, "Add export button", "Users asked for it"));

        Assert.Equal(ChangeKind.ObservationDriven, change.Kind);
        Assert.Equal(ChangeStatus.Captured, change.Status);

        var detail = await _changes.GetDetailAsync(change.Id);
        Assert.Single(detail.Evidence);
        Assert.Equal(obs.Id, detail.Evidence[0].Id);
        Assert.Equal(ObservationStatus.Promoted, detail.Evidence[0].Status);
    }

    [Fact]
    public async Task Promote_AsReporter_IsForbiddenByRoleGuard()
    {
        var seed = await SeedAsync();
        var reporter = await _projects.AddMemberAsync(seed.ProjectId,
            new AddMemberRequest("Rhea", ActorType.Human, null, MemberRole.Reporter));
        var obs = await _observations.IngestAsync(seed.SourceId,
            new IngestObservationRequest(ObservationKind.BehavioralFriction, "{}", null, null));

        await Assert.ThrowsAsync<InsufficientRoleException>(() =>
            _observations.PromoteAsync(obs.Id, new PromoteObservationRequest(reporter.ActorId, "x", null)));
    }

    [Fact]
    public async Task NonMember_CannotActOnProject()
    {
        var seed = await SeedAsync();
        var change = await _changes.CreateAsync(seed.ProjectId,
            new CreateChangeRequest(seed.ContributorId, "x", null, ChangeKind.Feature));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _changes.TransitionAsync(change.Id,
                new TransitionChangeRequest(Guid.NewGuid(), ChangeStatus.Triaged, null, null)));
    }

    [Fact]
    public async Task WhitelistGate_BlocksContributor_AllowsMaintainer()
    {
        var seed = await SeedAsync();
        var change = await _changes.CreateAsync(seed.ProjectId,
            new CreateChangeRequest(seed.ContributorId, "Gate test", null, ChangeKind.Feature));

        await _changes.TransitionAsync(change.Id,
            new TransitionChangeRequest(seed.ContributorId, ChangeStatus.Triaged, null, null));

        // Contributor blocked at the whitelist gate.
        await Assert.ThrowsAsync<InsufficientRoleException>(() =>
            _changes.TransitionAsync(change.Id,
                new TransitionChangeRequest(seed.ContributorId, ChangeStatus.Approved, null, null)));

        // Maintainer crosses it.
        var approved = await _changes.TransitionAsync(change.Id,
            new TransitionChangeRequest(seed.MaintainerId, ChangeStatus.Approved, "ok", null));
        Assert.Equal(ChangeStatus.Approved, approved.Status);
    }

    [Fact]
    public async Task FullPath_CaptureToDone_RecordsAuditTrail()
    {
        var seed = await SeedAsync();
        var change = await _changes.CreateAsync(seed.ProjectId,
            new CreateChangeRequest(seed.ContributorId, "End to end", null, ChangeKind.Feature));

        await _changes.TransitionAsync(change.Id, new TransitionChangeRequest(seed.ContributorId, ChangeStatus.Triaged, null, null));
        await _changes.TransitionAsync(change.Id, new TransitionChangeRequest(seed.MaintainerId, ChangeStatus.Approved, null, null));
        await _changes.TransitionAsync(change.Id, new TransitionChangeRequest(seed.ContributorId, ChangeStatus.InDevelopment, null, null));
        await _changes.TransitionAsync(change.Id, new TransitionChangeRequest(seed.ContributorId, ChangeStatus.InReview, null, null));
        var done = await _changes.TransitionAsync(change.Id, new TransitionChangeRequest(seed.MaintainerId, ChangeStatus.Done, null, null));

        Assert.Equal(ChangeStatus.Done, done.Status);

        var trail = await _changes.GetAuditAsync(change.Id);
        // Created + 5 transitions.
        Assert.Equal(6, trail.Count);
        Assert.Equal("Created", trail[0].Action);
        Assert.Equal("Done", trail[^1].ToStatus);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
