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
    public async Task ExportInDevelopment_IncludesOnlyInDevChanges_AsWorkOrder()
    {
        var seed = await SeedAsync();

        async Task<Guid> InDevWithPriority(string title, string desc, Priority priority)
        {
            var c = await _changes.CreateAsync(seed.ProjectId,
                new CreateChangeRequest(seed.MaintainerId, title, desc, ChangeKind.Feature, priority));
            foreach (var target in new[] { ChangeStatus.Triaged, ChangeStatus.Approved, ChangeStatus.InDevelopment })
            {
                await _changes.TransitionAsync(c.Id, new TransitionChangeRequest(seed.MaintainerId, target, null, null));
            }
            return c.Id;
        }

        await InDevWithPriority("Add CSV export", "Let users download a CSV.", Priority.High);
        await InDevWithPriority("Fix login redirect", "Redirect loops on expired session.", Priority.None);
        await _changes.CreateAsync(seed.ProjectId,
            new CreateChangeRequest(seed.MaintainerId, "Not started yet", "still captured", ChangeKind.Problem));

        var export = await _changes.ExportInDevelopmentAsync(seed.ProjectId);

        // Filename carries a short UTC timestamp: <slug>-in-development-yyyyMMdd-HHmmss.md
        Assert.Matches(@"-in-development-\d{8}-\d{6}\.md$", export.FileName);
        Assert.Contains("Claude Code", export.Markdown);
        Assert.Contains("Add CSV export", export.Markdown);
        Assert.Contains("Let users download a CSV.", export.Markdown);
        Assert.Contains("Fix login redirect", export.Markdown);
        Assert.DoesNotContain("Not started yet", export.Markdown);

        // Per-change git workflow instructions.
        Assert.Contains("integration branch", export.Markdown);
        Assert.Contains("git merge --no-ff", export.Markdown);
        Assert.Contains("Branch: `change/", export.Markdown);
        Assert.Contains("- [ ] Implemented, verified, committed, merged", export.Markdown);

        // Ordered by priority severity (High before None), not alphabetically by the stored string.
        Assert.True(
            export.Markdown.IndexOf("Add CSV export", StringComparison.Ordinal)
            < export.Markdown.IndexOf("Fix login redirect", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdateDetails_AsAuthor_EditsFieldsAndAudits()
    {
        var seed = await SeedAsync();
        var change = await _changes.CreateAsync(seed.ProjectId,
            new CreateChangeRequest(seed.ContributorId, "Original", "old", ChangeKind.Feature));

        var updated = await _changes.UpdateDetailsAsync(change.Id,
            new UpdateChangeDetailsRequest(seed.ContributorId, "Renamed", "new body", Priority.High));

        Assert.Equal("Renamed", updated.Title);
        Assert.Equal("new body", updated.Description);
        Assert.Equal(Priority.High, updated.Priority);

        var trail = await _changes.GetAuditAsync(change.Id);
        Assert.Contains(trail, e => e.Action == "DetailsEdited");
    }

    [Fact]
    public async Task UpdateDetails_AsMaintainerNonAuthor_Succeeds()
    {
        var seed = await SeedAsync();
        var change = await _changes.CreateAsync(seed.ProjectId,
            new CreateChangeRequest(seed.ContributorId, "Original", null, ChangeKind.Feature));

        var updated = await _changes.UpdateDetailsAsync(change.Id,
            new UpdateChangeDetailsRequest(seed.MaintainerId, "Maintainer edit", null, Priority.None));

        Assert.Equal("Maintainer edit", updated.Title);
    }

    [Fact]
    public async Task UpdateDetails_AsOtherContributor_IsForbidden()
    {
        var seed = await SeedAsync();
        var other = await _projects.AddMemberAsync(seed.ProjectId,
            new AddMemberRequest("Other", ActorType.Human, null, MemberRole.Contributor));
        var change = await _changes.CreateAsync(seed.ProjectId,
            new CreateChangeRequest(seed.ContributorId, "Original", null, ChangeKind.Feature));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _changes.UpdateDetailsAsync(change.Id,
                new UpdateChangeDetailsRequest(other.ActorId, "hijack", null, Priority.None)));
    }

    [Fact]
    public async Task UpdateDetails_AsNonMember_IsForbidden()
    {
        var seed = await SeedAsync();
        var change = await _changes.CreateAsync(seed.ProjectId,
            new CreateChangeRequest(seed.ContributorId, "Original", null, ChangeKind.Feature));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _changes.UpdateDetailsAsync(change.Id,
                new UpdateChangeDetailsRequest(Guid.NewGuid(), "x", null, Priority.None)));
    }

    [Fact]
    public async Task UpdateMember_ChangesRole_AndRemoveDropsMembershipButKeepsActor()
    {
        var seed = await SeedAsync();
        // The contributor authored a change, so their actor must survive removal.
        var change = await _changes.CreateAsync(seed.ProjectId,
            new CreateChangeRequest(seed.ContributorId, "By cory", null, ChangeKind.Feature));

        var updated = await _projects.UpdateMemberAsync(seed.ProjectId, seed.ContributorId,
            new UpdateMemberRequest("Cory Renamed", null, MemberRole.Maintainer, null));
        Assert.Equal("Cory Renamed", updated.DisplayName);
        Assert.Equal(MemberRole.Maintainer, updated.Role);

        await _projects.RemoveMemberAsync(seed.ProjectId, seed.ContributorId);
        var members = await _projects.ListMembersAsync(seed.ProjectId);
        Assert.DoesNotContain(members, m => m.ActorId == seed.ContributorId);

        // Actor row retained → the authored change still resolves.
        var detail = await _changes.GetDetailAsync(change.Id);
        Assert.Equal(seed.ContributorId, detail.Change.CreatedByActorId);
    }

    [Fact]
    public async Task UpdateLlmProvider_EditsFields()
    {
        var seed = await SeedAsync();
        var created = await _projects.CreateLlmProviderAsync(seed.ProjectId,
            new CreateLlmProviderRequest(LlmProviderType.Anthropic, "claude", null, "claude-haiku-4-5", "OLD_KEY"));

        var updated = await _projects.UpdateLlmProviderAsync(created.Id,
            new UpdateLlmProviderRequest(LlmProviderType.Anthropic, "claude", null, "claude-opus-4-8", "NEW_KEY", Enabled: false));

        Assert.Equal("claude-opus-4-8", updated.Model);
        Assert.Equal("NEW_KEY", updated.ApiKeySecretRef);
        Assert.False(updated.Enabled);
    }

    [Fact]
    public async Task DeleteLlmProvider_BlockedWhenInUse_ThenAllowed()
    {
        var seed = await SeedAsync();
        var llm = await _projects.CreateLlmProviderAsync(seed.ProjectId,
            new CreateLlmProviderRequest(LlmProviderType.Anthropic, "claude", null, "claude-sonnet-4-6", "K"));
        var agent = await _projects.AddMemberAsync(seed.ProjectId,
            new AddMemberRequest("Aria", ActorType.Agent, null, MemberRole.Contributor, llm.Id));

        await Assert.ThrowsAsync<BadRequestException>(() => _projects.DeleteLlmProviderAsync(llm.Id));

        // Unbind the agent, then deletion succeeds.
        await _projects.UpdateMemberAsync(seed.ProjectId, agent.ActorId,
            new UpdateMemberRequest("Aria", null, MemberRole.Contributor, null));
        await _projects.DeleteLlmProviderAsync(llm.Id);

        var providers = await _projects.ListLlmProvidersAsync(seed.ProjectId);
        Assert.DoesNotContain(providers, p => p.Id == llm.Id);
    }

    [Fact]
    public async Task Actors_OrphanAgent_BecomesDeletable_AndDeletes()
    {
        var seed = await SeedAsync();
        var llm = await _projects.CreateLlmProviderAsync(seed.ProjectId,
            new CreateLlmProviderRequest(LlmProviderType.Anthropic, "claude", null, "claude-sonnet-4-6", "K"));
        var agent = await _projects.AddMemberAsync(seed.ProjectId,
            new AddMemberRequest("Aria", ActorType.Agent, null, MemberRole.Contributor, llm.Id));

        var listed = await _projects.ListActorsAsync(agentsOnly: true);
        var row = listed.Single(a => a.Id == agent.ActorId);
        Assert.Equal(1, row.ProjectCount);
        Assert.False(row.Deletable);
        Assert.Equal("claude", row.ProviderName);

        await _projects.RemoveMemberAsync(seed.ProjectId, agent.ActorId);
        var afterRemove = (await _projects.ListActorsAsync(agentsOnly: true)).Single(a => a.Id == agent.ActorId);
        Assert.Equal(0, afterRemove.ProjectCount);
        Assert.True(afterRemove.Deletable);

        await _projects.DeleteActorAsync(agent.ActorId);
        Assert.DoesNotContain(await _projects.ListActorsAsync(agentsOnly: true), a => a.Id == agent.ActorId);
    }

    [Fact]
    public async Task DeleteActor_BlockedWhenReferenced()
    {
        var seed = await SeedAsync();
        // The contributor authors a change, then leaves the project — actor is orphaned but referenced.
        await _changes.CreateAsync(seed.ProjectId,
            new CreateChangeRequest(seed.ContributorId, "By cory", null, ChangeKind.Feature));
        await _projects.RemoveMemberAsync(seed.ProjectId, seed.ContributorId);

        await Assert.ThrowsAsync<BadRequestException>(() => _projects.DeleteActorAsync(seed.ContributorId));
    }

    [Fact]
    public async Task GlobalLlmProvider_IsAvailableToEveryProject()
    {
        var global = await _projects.CreateLlmProviderAsync(
            null, new CreateLlmProviderRequest(LlmProviderType.Anthropic, "shared-claude", null, "claude-sonnet-4-6", "ANTHROPIC_KEY"));
        Assert.Null(global.ProjectId);

        var project = await _projects.CreateAsync(new CreateProjectRequest("Fresh", null));
        var available = await _projects.ListLlmProvidersAsync(project.Id);

        Assert.Contains(available, p => p.Id == global.Id && p.ProjectId == null);
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
        // Created + 5 transitions. Assert presence (timestamps can tie under rapid transitions,
        // so don't depend on positional order).
        Assert.Equal(6, trail.Count);
        Assert.Contains(trail, e => e.Action == "Created");
        Assert.Contains(trail, e => e.ToStatus == "Done");
        Assert.Contains(trail, e => e.FromStatus == "Triaged" && e.ToStatus == "Approved");
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
