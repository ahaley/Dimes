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

/// <summary>Work-order ingest: an agent reports what it did, Dimes attaches the evidence and flags the
/// item — and never changes a status. The containment rules matter as much as the happy path: the token is
/// the only gate, so what it can and can't reach is the security boundary.</summary>
public sealed class WorkOrderIngestServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ChangeRequestService _changes;
    private readonly WorkOrderService _workOrders;
    private readonly FakeBoardNotifier _notifier = new();

    private Guid _projectId;
    private Guid _exporterId;
    private int _nextNumber = 1;

    public WorkOrderIngestServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();

        var resolver = new MembershipResolver(_db);
        _changes = new ChangeRequestService(_db, new LifecycleService(), resolver, _notifier);
        _workOrders = new WorkOrderService(_db, resolver, _notifier);
    }

    private async Task<(Guid ProjectId, Guid ActorId)> SeedProjectAsync(string name = "Demo")
    {
        var project = new Project { Name = name, Key = name.ToUpperInvariant()[..3] };
        var actor = new Actor { DisplayName = "Mae", Type = ActorType.Human };
        _db.Projects.Add(project);
        _db.Actors.Add(actor);
        _db.Memberships.Add(new Membership
        {
            ProjectId = project.Id,
            ActorId = actor.Id,
            Role = MemberRole.Maintainer,
        });
        await _db.SaveChangesAsync();
        return (project.Id, actor.Id);
    }

    private async Task<ChangeRequest> InDevAsync(string title, Guid? projectId = null)
    {
        var change = new ChangeRequest
        {
            ProjectId = projectId ?? _projectId,
            Title = title,
            Status = ChangeStatus.InDevelopment,
            CreatedByActorId = _exporterId,
            Number = _nextNumber++,
        };
        _db.ChangeRequests.Add(change);
        await _db.SaveChangesAsync();
        return change;
    }

    /// <summary>Export for real, then recover the token from the markdown — the same path an agent walks,
    /// so these tests break if the emitted contract and the parser ever drift apart.</summary>
    private async Task<string> ExportAndTakeTokenAsync()
    {
        var export = await _changes.ExportInDevelopmentAsync(_projectId, _exporterId, "https://dimes.test");
        var match = System.Text.RegularExpressions.Regex.Match(
            export.Markdown, @"/api/work-orders/(?<token>[A-Za-z0-9_-]+)/results");
        Assert.True(match.Success, "The export should embed a report-back URL carrying the token.");
        return match.Groups["token"].Value;
    }

    private async Task SetupAsync()
    {
        (_projectId, _exporterId) = await SeedProjectAsync();
    }

    private static string Branch(ChangeRequest c, string slug) => $"change/{c.Id.ToString()[..8]}-{slug}";

    private static WorkOrderResultsRequest Report(
        IReadOnlyList<WorkOrderCommitReport>? commits = null,
        IReadOnlyList<WorkOrderPullRequestReport>? prs = null,
        IReadOnlyList<WorkOrderBlockedReport>? blocked = null,
        string? summary = null) => new(summary, commits, prs, blocked);

    [Fact]
    public async Task Report_WithATrailerAndUrl_LinksTheCommitCommentsAndFlagsTheItem()
    {
        await SetupAsync();
        var change = await InDevAsync("Add CSV export");
        var token = await ExportAndTakeTokenAsync();
        _notifier.Events.Clear();

        var result = await _workOrders.ReportResultsAsync(token, Report(
            commits: [new WorkOrderCommitReport(
                "a1b2c3d4e5", $"Add CSV export\n\nDimes change {change.Id}", null,
                "https://github.com/acme/app/commit/a1b2c3d4e5")],
            summary: "All done."));

        Assert.Equal(1, result.ReportedCount);
        Assert.Empty(result.Ignored);
        // The response tells the agent what its report actually did; a wrong count here is a lie to the
        // only caller that can act on it.
        Assert.Equal(1, Assert.Single(result.Items).LinksAdded);

        var link = Assert.Single(await _db.ScmLinks.ToListAsync());
        Assert.Equal("https://github.com/acme/app/commit/a1b2c3d4e5", link.Url);
        Assert.Equal(change.Id, link.ChangeRequestId);

        var comment = Assert.Single(await _db.Comments.ToListAsync());
        Assert.Equal(CommentKind.AgentRecommendation, comment.Kind);
        // Attributed to the exporting human: no agent token exists, so an agent-supplied identity would
        // be unauthenticated self-attribution.
        Assert.Equal(_exporterId, comment.AuthorActorId);
        Assert.Contains("a1b2c3d4e5", comment.Body);
        Assert.Contains("All done.", comment.Body);

        var item = Assert.Single(await _db.WorkOrderItems.ToListAsync());
        Assert.Equal(WorkOrderItemStatus.Reported, item.Status);
        Assert.NotNull(item.ReportedAt);

        // The status itself must not have moved — ingest is recommend-only.
        Assert.Equal(ChangeStatus.InDevelopment, (await _db.ChangeRequests.FindAsync(change.Id))!.Status);
        Assert.Contains(_notifier.Events, e => e.ChangeId == change.Id && e.Kind == "reported");
    }

    [Fact]
    public async Task Report_CoveringOneChangeAndBlockingAnother_CountsBothChangesLinks()
    {
        await SetupAsync();
        var done = await InDevAsync("Add CSV export");
        var stuck = await InDevAsync("Fix login redirect");
        var token = await ExportAndTakeTokenAsync();

        var result = await _workOrders.ReportResultsAsync(token, Report(
            commits: [new WorkOrderCommitReport(
                "a1b2c3d4e5f6", $"Add CSV export\n\nDimes change {done.Id}", null,
                "https://github.com/acme/app/commit/a1b2c3d4e5f6")],
            prs: [new WorkOrderPullRequestReport("https://github.com/acme/app/pull/42", null, done.Id)],
            blocked: [new WorkOrderBlockedReport(stuck.Id, "Needs a product decision.")],
            summary: "Integrated 1 of 2."));

        Assert.Equal(1, result.ReportedCount);
        Assert.Equal(1, result.BlockedCount);
        Assert.Equal(2, await _db.ScmLinks.CountAsync());
        // The commit url and the PR url both landed, so the agent should be told both landed.
        Assert.Equal(2, result.Items.Single(i => i.ChangeId == done.Id).LinksAdded);
    }

    [Fact]
    public async Task Report_WithAnUnknownToken_IsNotFound()
    {
        await SetupAsync();

        await Assert.ThrowsAsync<NotFoundException>(
            () => _workOrders.ReportResultsAsync("not-a-real-token", Report()));
    }

    [Fact]
    public async Task Report_WithAnExpiredToken_IsNotFound()
    {
        await SetupAsync();
        await InDevAsync("Add CSV export");
        var token = await ExportAndTakeTokenAsync();
        var workOrder = await _db.WorkOrders.FirstAsync();
        workOrder.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        // Same error as an unknown token: a distinct "expired" would confirm to the holder of a leaked
        // stale token that it was once real.
        await Assert.ThrowsAsync<NotFoundException>(() => _workOrders.ReportResultsAsync(token, Report()));
    }

    [Fact]
    public async Task Report_ForAChangeOutsideThisWorkOrder_IsIgnoredEntirely()
    {
        await SetupAsync();
        await InDevAsync("Add CSV export");
        var token = await ExportAndTakeTokenAsync();
        // Exported after the work order was minted, so it's in the project but not in this order.
        var later = await InDevAsync("Fix login redirect");

        var result = await _workOrders.ReportResultsAsync(token, Report(
            commits: [new WorkOrderCommitReport(
                "beef", $"Fix login redirect\n\nDimes change {later.Id}", null,
                "https://github.com/acme/app/commit/beef")]));

        // The token's scope IS the work order: honoring this would widen a per-export capability into a
        // project-wide write.
        Assert.Single(result.Ignored);
        Assert.Empty(await _db.ScmLinks.ToListAsync());
        Assert.Empty(await _db.Comments.ToListAsync());
        Assert.All(await _db.WorkOrderItems.ToListAsync(),
            i => Assert.Equal(WorkOrderItemStatus.Pending, i.Status));
    }

    [Fact]
    public async Task Report_ForAChangeInAnotherProject_IsIgnoredTheSameWay()
    {
        await SetupAsync();
        await InDevAsync("Add CSV export");
        var token = await ExportAndTakeTokenAsync();

        var (otherProjectId, otherActorId) = await SeedProjectAsync("Other");
        var foreign = new ChangeRequest
        {
            ProjectId = otherProjectId,
            Title = "Someone else's change",
            Status = ChangeStatus.InDevelopment,
            CreatedByActorId = otherActorId,
            Number = 1,
        };
        _db.ChangeRequests.Add(foreign);
        await _db.SaveChangesAsync();

        var result = await _workOrders.ReportResultsAsync(token, Report(
            commits: [new WorkOrderCommitReport("beef", $"x\n\nDimes change {foreign.Id}", null, null)]));

        Assert.Single(result.Ignored);
        Assert.Empty(await _db.Comments.ToListAsync());
    }

    [Fact]
    public async Task Report_WithAShaButNoUrl_CommentsWithoutCreatingALink()
    {
        await SetupAsync();
        var change = await InDevAsync("Add CSV export");
        var token = await ExportAndTakeTokenAsync();

        await _workOrders.ReportResultsAsync(token, Report(
            commits: [new WorkOrderCommitReport("a1b2c3d", $"Add CSV export\n\nDimes change {change.Id}", null, null)]));

        // A bare sha isn't linkable — Dimes stores no repo base URL — so it belongs in the comment.
        Assert.Empty(await _db.ScmLinks.ToListAsync());
        Assert.Contains("a1b2c3d", Assert.Single(await _db.Comments.ToListAsync()).Body);
        Assert.Equal(WorkOrderItemStatus.Reported, (await _db.WorkOrderItems.FirstAsync()).Status);
    }

    [Fact]
    public async Task Report_WithAJavascriptUrl_SkipsTheLinkButStillRecordsTheReport()
    {
        await SetupAsync();
        var change = await InDevAsync("Add CSV export");
        var token = await ExportAndTakeTokenAsync();

        await _workOrders.ReportResultsAsync(token, Report(
            commits: [new WorkOrderCommitReport(
                "a1b2c3d", $"Add CSV export\n\nDimes change {change.Id}", null, "javascript:alert(1)")]));

        // Stored URLs render as an <a href> in the SPA; one bad URL shouldn't reject the whole run either.
        Assert.Empty(await _db.ScmLinks.ToListAsync());
        Assert.Equal(WorkOrderItemStatus.Reported, (await _db.WorkOrderItems.FirstAsync()).Status);
    }

    [Fact]
    public async Task Report_WithABlockedEntry_FlagsBlockedAndPromptsNothing()
    {
        await SetupAsync();
        var change = await InDevAsync("Add CSV export");
        var token = await ExportAndTakeTokenAsync();

        var result = await _workOrders.ReportResultsAsync(token, Report(
            blocked: [new WorkOrderBlockedReport(change.Id, "Needs a product decision on the empty state.")]));

        Assert.Equal(1, result.BlockedCount);
        Assert.Equal(0, result.ReportedCount);
        var item = await _db.WorkOrderItems.FirstAsync();
        Assert.Equal(WorkOrderItemStatus.Blocked, item.Status);
        Assert.Contains("product decision", Assert.Single(await _db.Comments.ToListAsync()).Body);
    }

    [Fact]
    public async Task Report_WithBothCommitsAndABlockForTheSameChange_LandsBlocked()
    {
        await SetupAsync();
        var change = await InDevAsync("Add CSV export");
        var token = await ExportAndTakeTokenAsync();

        await _workOrders.ReportResultsAsync(token, Report(
            commits: [new WorkOrderCommitReport(
                "a1b2c3d", $"Partial work\n\nDimes change {change.Id}", null,
                "https://github.com/acme/app/commit/a1b2c3d")],
            blocked: [new WorkOrderBlockedReport(change.Id, "Couldn't finish — conflicts.")]));

        // An agent that committed partial work and then gave up needs a human, not an In Review prompt.
        Assert.Equal(WorkOrderItemStatus.Blocked, (await _db.WorkOrderItems.FirstAsync()).Status);
    }

    [Fact]
    public async Task Report_SentTwiceWithTheSameBody_ChangesNothingTheSecondTime()
    {
        await SetupAsync();
        var change = await InDevAsync("Add CSV export");
        var token = await ExportAndTakeTokenAsync();
        var body = Report(
            commits: [new WorkOrderCommitReport(
                "a1b2c3d", $"Add CSV export\n\nDimes change {change.Id}", null,
                "https://github.com/acme/app/commit/a1b2c3d")]);

        await _workOrders.ReportResultsAsync(token, body);
        await _workOrders.ReportResultsAsync(token, body);

        // Agents retry. A replay must be a true no-op, or every retry spams the change.
        Assert.Single(await _db.ScmLinks.ToListAsync());
        Assert.Single(await _db.Comments.ToListAsync());
        Assert.Equal(WorkOrderItemStatus.Reported, (await _db.WorkOrderItems.FirstAsync()).Status);
    }

    [Fact]
    public async Task Report_FollowedByGenuinelyNewWork_IsRecordedAgain()
    {
        await SetupAsync();
        var change = await InDevAsync("Add CSV export");
        var token = await ExportAndTakeTokenAsync();

        await _workOrders.ReportResultsAsync(token, Report(
            commits: [new WorkOrderCommitReport(
                "a1b2c3d", $"First\n\nDimes change {change.Id}", null,
                "https://github.com/acme/app/commit/a1b2c3d")]));
        await _workOrders.ReportResultsAsync(token, Report(
            commits: [new WorkOrderCommitReport(
                "9f8e7d6", $"Second\n\nDimes change {change.Id}", null,
                "https://github.com/acme/app/commit/9f8e7d6")]));

        Assert.Equal(2, await _db.ScmLinks.CountAsync());
        Assert.Equal(2, await _db.Comments.CountAsync());
    }

    [Fact]
    public async Task Report_WithoutATrailer_FallsBackToTheExactBranchNameWeMinted()
    {
        await SetupAsync();
        var change = await InDevAsync("Add CSV export");
        var token = await ExportAndTakeTokenAsync();
        var branch = (await _db.WorkOrderItems.FirstAsync()).BranchName;

        await _workOrders.ReportResultsAsync(token, Report(
            commits: [new WorkOrderCommitReport("a1b2c3d", "Add CSV export", branch, null)]));

        Assert.Equal(WorkOrderItemStatus.Reported, (await _db.WorkOrderItems.FirstAsync()).Status);
        // The weaker provenance is stated, so the human confirming can judge it.
        Assert.Contains("matched by branch", Assert.Single(await _db.Comments.ToListAsync()).Body);
    }

    [Fact]
    public async Task Report_WithARenamedBranch_StillMatchesOnTheIdPrefix()
    {
        await SetupAsync();
        var change = await InDevAsync("Add CSV export");
        var token = await ExportAndTakeTokenAsync();

        await _workOrders.ReportResultsAsync(token, Report(
            commits: [new WorkOrderCommitReport("a1b2c3d", "Add CSV export", Branch(change, "renamed-slug"), null)]));

        Assert.Equal(WorkOrderItemStatus.Reported, (await _db.WorkOrderItems.FirstAsync()).Status);
    }

    [Fact]
    public async Task Report_WithATrailer_IgnoresTheBranchEntirely()
    {
        await SetupAsync();
        var trailered = await InDevAsync("Add CSV export");
        var other = await InDevAsync("Fix login redirect");
        var token = await ExportAndTakeTokenAsync();

        // The trailer is authoritative: a mismatched branch must not steal the claim.
        await _workOrders.ReportResultsAsync(token, Report(
            commits: [new WorkOrderCommitReport(
                "a1b2c3d", $"Add CSV export\n\nDimes change {trailered.Id}", Branch(other, "fix-login-redirect"), null)]));

        var items = await _db.WorkOrderItems.ToListAsync();
        Assert.Equal(WorkOrderItemStatus.Reported, items.Single(i => i.ChangeRequestId == trailered.Id).Status);
        Assert.Equal(WorkOrderItemStatus.Pending, items.Single(i => i.ChangeRequestId == other.Id).Status);
    }

    [Fact]
    public async Task Report_FromAnExporterWhoLeftTheProject_IsForbidden()
    {
        await SetupAsync();
        await InDevAsync("Add CSV export");
        var token = await ExportAndTakeTokenAsync();

        _db.Memberships.RemoveRange(await _db.Memberships.Where(m => m.ActorId == _exporterId).ToListAsync());
        await _db.SaveChangesAsync();

        // Removing the exporter revokes their outstanding work orders — the revocation lever.
        await Assert.ThrowsAsync<ForbiddenException>(() => _workOrders.ReportResultsAsync(token, Report()));
    }

    [Fact]
    public async Task Report_WithTooManyCommits_IsRejected()
    {
        await SetupAsync();
        await InDevAsync("Add CSV export");
        var token = await ExportAndTakeTokenAsync();
        var commits = Enumerable.Range(0, 201)
            .Select(i => new WorkOrderCommitReport($"sha{i}", "x", null, null))
            .ToList();

        // The endpoint is anonymous, so a valid token still can't be used to flood the database.
        await Assert.ThrowsAsync<BadRequestException>(
            () => _workOrders.ReportResultsAsync(token, Report(commits: commits)));
    }

    [Fact]
    public async Task Report_WithAPullRequestUrl_LinksItToTheNamedChange()
    {
        await SetupAsync();
        var change = await InDevAsync("Add CSV export");
        var token = await ExportAndTakeTokenAsync();

        await _workOrders.ReportResultsAsync(token, Report(
            prs: [new WorkOrderPullRequestReport("https://github.com/acme/app/pull/42", null, change.Id)]));

        var link = Assert.Single(await _db.ScmLinks.ToListAsync());
        Assert.Equal("https://github.com/acme/app/pull/42", link.Url);
        Assert.Equal(WorkOrderItemStatus.Reported, (await _db.WorkOrderItems.FirstAsync()).Status);
    }

    [Fact]
    public async Task Latest_ReportsHowMuchOfTheExportHasComeBack()
    {
        await SetupAsync();
        var one = await InDevAsync("Add CSV export");
        await InDevAsync("Fix login redirect");
        var token = await ExportAndTakeTokenAsync();

        await _workOrders.ReportResultsAsync(token, Report(
            commits: [new WorkOrderCommitReport("a1b2c3d", $"x\n\nDimes change {one.Id}", null, null)]));

        var summary = await _workOrders.LatestAsync(_projectId);

        Assert.NotNull(summary);
        Assert.Equal(2, summary.ItemCount);
        Assert.Equal(1, summary.ReportedCount);
        Assert.Single(summary.PendingChangeIds);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
