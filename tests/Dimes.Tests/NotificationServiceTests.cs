using System.Text.RegularExpressions;
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

/// <summary>Outbound notifications are a projection of the audit log: the event hooks stage outbox
/// deliveries in the same transaction as the change, gated by which channels subscribe to the event.
/// These tests assert the right deliveries are enqueued (and, as importantly, when none are) — the drain
/// worker and the Google Chat wire call are exercised end-to-end, not here.</summary>
public sealed class NotificationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ProjectService _projects;
    private readonly ChangeRequestService _changes;
    private readonly WorkOrderService _workOrders;
    private readonly FakeBoardNotifier _notifier = new();

    public NotificationServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();

        var lifecycle = new LifecycleService();
        var resolver = new MembershipResolver(_db);
        _projects = new ProjectService(_db, resolver);
        _changes = new ChangeRequestService(_db, lifecycle, resolver, _notifier, new NotificationDispatcher(_db));
        _workOrders = new WorkOrderService(_db, resolver, _notifier, new NotificationDispatcher(_db));
    }

    private async Task<(Guid ProjectId, Guid MaintainerId, Guid ContributorId)> SeedAsync()
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("Demo", null));
        var maintainer = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Maud", ActorType.Human, "maud@x.com", MemberRole.Maintainer));
        var contributor = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Cory", ActorType.Human, "cory@x.com", MemberRole.Contributor));
        return (project.Id, maintainer.ActorId, contributor.ActorId);
    }

    private async Task<NotificationChannelDto> AddChannelAsync(
        Guid projectId, params NotificationEventType[] events) =>
        await _projects.CreateNotificationChannelAsync(projectId, new CreateNotificationChannelRequest(
            NotificationChannelType.GoogleChat, "team-space", "spaces/AAAA", "GCHAT_CREDS", events));

    private async Task<List<NotificationDelivery>> DeliveriesAsync(NotificationEventType? ofEvent = null)
    {
        var query = _db.NotificationDeliveries.AsNoTracking().AsQueryable();
        if (ofEvent is not null)
        {
            query = query.Where(d => d.Event == ofEvent);
        }
        return await query.ToListAsync();
    }

    [Fact]
    public async Task Channel_Crud_RoundTrips()
    {
        var seed = await SeedAsync();
        var created = await AddChannelAsync(seed.ProjectId, NotificationEventType.AwaitingApproval);

        Assert.Equal("team-space", created.Name);
        Assert.Equal("spaces/AAAA", created.Target);
        Assert.Contains(NotificationEventType.AwaitingApproval, created.Events);
        Assert.True(created.Enabled);

        var updated = await _projects.UpdateNotificationChannelAsync(seed.ProjectId, created.Id,
            new UpdateNotificationChannelRequest(NotificationChannelType.GoogleChat, "renamed", "spaces/BBBB",
                "GCHAT_CREDS", [NotificationEventType.AssignedToYou, NotificationEventType.DailyDigest], Enabled: false));
        Assert.Equal("renamed", updated.Name);
        Assert.False(updated.Enabled);
        Assert.DoesNotContain(NotificationEventType.AwaitingApproval, updated.Events);
        Assert.Contains(NotificationEventType.DailyDigest, updated.Events);

        var listed = await _projects.ListNotificationChannelsAsync(seed.ProjectId);
        Assert.Single(listed);

        await _projects.DeleteNotificationChannelAsync(seed.ProjectId, created.Id);
        Assert.Empty(await _projects.ListNotificationChannelsAsync(seed.ProjectId));
    }

    [Fact]
    public async Task Create_RejectsMissingTarget()
    {
        var seed = await SeedAsync();
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _projects.CreateNotificationChannelAsync(seed.ProjectId, new CreateNotificationChannelRequest(
                NotificationChannelType.GoogleChat, "team", "  ", "GCHAT_CREDS", [NotificationEventType.AwaitingApproval])));
    }

    [Fact]
    public async Task Create_RejectsMissingSecretRef()
    {
        var seed = await SeedAsync();
        // Google Chat can't authenticate without credentials, so an empty reference must fail at save —
        // not silently produce an undeliverable channel that only fails minutes later at send time.
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _projects.CreateNotificationChannelAsync(seed.ProjectId, new CreateNotificationChannelRequest(
                NotificationChannelType.GoogleChat, "team", "spaces/AAAA", "  ", [NotificationEventType.AwaitingApproval])));
    }

    [Fact]
    public async Task AwaitingApproval_EnqueuesWhenChangeEntersTriaged()
    {
        var seed = await SeedAsync();
        await AddChannelAsync(seed.ProjectId, NotificationEventType.AwaitingApproval);
        var change = await _changes.CreateAsync(seed.ProjectId, seed.ContributorId,
            new CreateChangeRequest("Gate me", null, ChangeKind.Feature));

        await _changes.TransitionAsync(change.Id, seed.ContributorId,
            new TransitionChangeRequest(ChangeStatus.Triaged, null, null));

        var delivery = Assert.Single(await DeliveriesAsync(NotificationEventType.AwaitingApproval));
        Assert.Equal(change.Id, delivery.ChangeRequestId);
        Assert.Equal(NotificationDeliveryStatus.Pending, delivery.Status);
        Assert.Contains("awaiting a Maintainer's approval", delivery.Body);
    }

    [Fact]
    public async Task AssignedToYou_EnqueuesForRecipient_ButNotOnSelfAssign()
    {
        var seed = await SeedAsync();
        await AddChannelAsync(seed.ProjectId, NotificationEventType.AssignedToYou);
        var change = await _changes.CreateAsync(seed.ProjectId, seed.ContributorId,
            new CreateChangeRequest("Work", null, ChangeKind.Feature));

        // Contributor directs it to the Maintainer → one delivery, addressed to the Maintainer.
        await _changes.AssignAsync(change.Id, seed.ContributorId, new AssignChangeRequest(seed.MaintainerId));
        var delivery = Assert.Single(await DeliveriesAsync(NotificationEventType.AssignedToYou));
        Assert.Equal(seed.MaintainerId, delivery.RecipientActorId);

        // Contributor claims it themselves → no new delivery (you don't notify yourself).
        await _changes.AssignAsync(change.Id, seed.ContributorId, new AssignChangeRequest(seed.ContributorId));
        Assert.Single(await DeliveriesAsync(NotificationEventType.AssignedToYou));
    }

    [Fact]
    public async Task WorkOrderResults_EnqueuesForExporter_OnAgentReport()
    {
        var seed = await SeedAsync();
        await AddChannelAsync(seed.ProjectId, NotificationEventType.WorkOrderResults);

        // Drive a change to In Development, then export a work order as the Maintainer.
        var change = await _changes.CreateAsync(seed.ProjectId, seed.ContributorId,
            new CreateChangeRequest("Ship it", null, ChangeKind.Feature));
        foreach (var target in new[] { ChangeStatus.Triaged, ChangeStatus.Approved, ChangeStatus.InDevelopment })
        {
            await _changes.TransitionAsync(change.Id, seed.MaintainerId, new TransitionChangeRequest(target, null, null));
        }
        var export = await _changes.ExportInDevelopmentAsync(seed.ProjectId, seed.MaintainerId, "https://dimes.test");
        var token = Regex.Match(export.Markdown, @"/api/work-orders/(?<token>[A-Za-z0-9_-]+)/results").Groups["token"].Value;

        await _workOrders.ReportResultsAsync(token, new WorkOrderResultsRequest(
            "Done.",
            [new WorkOrderCommitReport("a1b2c3d", $"Ship it\n\nDimes change {change.Id}", null, "https://github.com/x/y/commit/a1b2c3d")],
            null, null));

        var delivery = Assert.Single(await DeliveriesAsync(NotificationEventType.WorkOrderResults));
        Assert.Equal(seed.MaintainerId, delivery.RecipientActorId); // the exporter
        Assert.Contains("received an agent report", delivery.Body);
    }

    [Fact]
    public async Task UnsubscribedEvent_ProducesNoDelivery()
    {
        var seed = await SeedAsync();
        // Channel routes only work-order results; a Triaged transition must not reach it.
        await AddChannelAsync(seed.ProjectId, NotificationEventType.WorkOrderResults);
        var change = await _changes.CreateAsync(seed.ProjectId, seed.ContributorId,
            new CreateChangeRequest("x", null, ChangeKind.Feature));

        await _changes.TransitionAsync(change.Id, seed.ContributorId,
            new TransitionChangeRequest(ChangeStatus.Triaged, null, null));

        Assert.Empty(await DeliveriesAsync(NotificationEventType.AwaitingApproval));
    }

    [Fact]
    public async Task DisabledChannel_ProducesNoDelivery()
    {
        var seed = await SeedAsync();
        var channel = await AddChannelAsync(seed.ProjectId, NotificationEventType.AwaitingApproval);
        await _projects.UpdateNotificationChannelAsync(seed.ProjectId, channel.Id,
            new UpdateNotificationChannelRequest(NotificationChannelType.GoogleChat, "team-space", "spaces/AAAA",
                "GCHAT_CREDS", [NotificationEventType.AwaitingApproval], Enabled: false));
        var change = await _changes.CreateAsync(seed.ProjectId, seed.ContributorId,
            new CreateChangeRequest("x", null, ChangeKind.Feature));

        await _changes.TransitionAsync(change.Id, seed.ContributorId,
            new TransitionChangeRequest(ChangeStatus.Triaged, null, null));

        Assert.Empty(await DeliveriesAsync());
    }

    [Fact]
    public async Task Preference_ProjectScopedOptOut_RoundTrips()
    {
        var seed = await SeedAsync();

        // Default is opted in.
        Assert.False((await _projects.GetNotificationPreferenceAsync(seed.MaintainerId, seed.ProjectId)).DigestOptOut);

        await _projects.UpdateNotificationPreferenceAsync(seed.MaintainerId, seed.ProjectId, digestOptOut: true);
        Assert.True((await _projects.GetNotificationPreferenceAsync(seed.MaintainerId, seed.ProjectId)).DigestOptOut);

        // Upsert flips it back without creating a second row.
        await _projects.UpdateNotificationPreferenceAsync(seed.MaintainerId, seed.ProjectId, digestOptOut: false);
        Assert.False((await _projects.GetNotificationPreferenceAsync(seed.MaintainerId, seed.ProjectId)).DigestOptOut);
        Assert.Single(_db.NotificationPreferences.Where(p => p.ActorId == seed.MaintainerId));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
