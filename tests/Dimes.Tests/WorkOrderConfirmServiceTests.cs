using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Domain.Lifecycle;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>Confirming an agent's report is not a new verb: moving a change to In Review through the
/// normal guarded transition is what settles the claim. That reuse is deliberate — a dedicated confirm
/// endpoint would be a second door into the state machine.</summary>
public sealed class WorkOrderConfirmServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ChangeRequestService _changes;

    private Guid _projectId;
    private Guid _actorId;
    private int _nextNumber = 1;

    public WorkOrderConfirmServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();

        var resolver = new MembershipResolver(_db);
        _changes = new ChangeRequestService(_db, new LifecycleService(), resolver, new FakeBoardNotifier(), new NotificationDispatcher(_db));
    }

    private async Task SetupAsync()
    {
        var project = new Project { Name = "Demo" };
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
        _projectId = project.Id;
        _actorId = actor.Id;
    }

    private async Task<ChangeRequest> ChangeAsync(
        string title, ChangeStatus status, ChangeKind kind = ChangeKind.Feature, Guid? parentId = null)
    {
        var change = new ChangeRequest
        {
            ProjectId = _projectId,
            Title = title,
            Kind = kind,
            Status = status,
            CreatedByActorId = _actorId,
            Number = _nextNumber++,
            ParentChangeRequestId = parentId,
        };
        _db.ChangeRequests.Add(change);
        await _db.SaveChangesAsync();
        return change;
    }

    /// <summary>An outstanding claim against a change, as ingest would have left it.</summary>
    private async Task<WorkOrderItem> ClaimAsync(ChangeRequest change, WorkOrderItemStatus status)
    {
        var workOrder = new WorkOrder
        {
            ProjectId = _projectId,
            ExportedByActorId = _actorId,
            FileName = "demo.md",
            TokenHash = WorkOrderToken.Hash(WorkOrderToken.Mint()),
        };
        var item = new WorkOrderItem
        {
            ChangeRequestId = change.Id,
            TitleSnapshot = change.Title,
            BranchName = $"change/{change.Id.ToString()[..8]}-slug",
            Status = status,
            ReportedAt = status == WorkOrderItemStatus.Pending ? null : DateTimeOffset.UtcNow,
        };
        workOrder.Items.Add(item);
        _db.WorkOrders.Add(workOrder);
        await _db.SaveChangesAsync();
        return item;
    }

    private Task Transition(Guid id, ChangeStatus target) =>
        _changes.TransitionAsync(id, _actorId, new TransitionChangeRequest(target, null, null));

    [Fact]
    public async Task MovingToInReview_SettlesTheReportedClaim()
    {
        await SetupAsync();
        var change = await ChangeAsync("Add CSV export", ChangeStatus.InDevelopment);
        var item = await ClaimAsync(change, WorkOrderItemStatus.Reported);

        await Transition(change.Id, ChangeStatus.InReview);

        await _db.Entry(item).ReloadAsync();
        Assert.Equal(WorkOrderItemStatus.Confirmed, item.Status);
    }

    [Fact]
    public async Task MovingToInReview_LeavesABlockedClaimAlone()
    {
        await SetupAsync();
        var change = await ChangeAsync("Add CSV export", ChangeStatus.InDevelopment);
        var item = await ClaimAsync(change, WorkOrderItemStatus.Blocked);

        await Transition(change.Id, ChangeStatus.InReview);

        // Blocked was never a claim of completion, so there's nothing to confirm.
        await _db.Entry(item).ReloadAsync();
        Assert.Equal(WorkOrderItemStatus.Blocked, item.Status);
    }

    [Fact]
    public async Task MovingAnEpicToInReview_SettlesItsChildrensClaims()
    {
        await SetupAsync();
        var epic = await ChangeAsync("Epic", ChangeStatus.InDevelopment, ChangeKind.Epic);
        var child = await ChangeAsync("Child", ChangeStatus.InDevelopment, parentId: epic.Id);
        var childItem = await ClaimAsync(child, WorkOrderItemStatus.Reported);

        await Transition(epic.Id, ChangeStatus.InReview);

        // The cascade moves the child's status, so it must settle the child's claim too — otherwise the
        // card keeps prompting for a change that already moved.
        await _db.Entry(childItem).ReloadAsync();
        Assert.Equal(WorkOrderItemStatus.Confirmed, childItem.Status);
    }

    [Fact]
    public async Task ReopeningFromInReview_DoesNotUnconfirmTheOldClaim()
    {
        await SetupAsync();
        var change = await ChangeAsync("Add CSV export", ChangeStatus.InDevelopment);
        var item = await ClaimAsync(change, WorkOrderItemStatus.Reported);
        await Transition(change.Id, ChangeStatus.InReview);

        await Transition(change.Id, ChangeStatus.InDevelopment);

        // A settled claim stays settled: a stale report must not re-prompt after someone reopens the
        // change for more work.
        await _db.Entry(item).ReloadAsync();
        Assert.Equal(WorkOrderItemStatus.Confirmed, item.Status);
    }

    [Fact]
    public async Task MovingSomewhereOtherThanInReview_LeavesTheClaimOutstanding()
    {
        await SetupAsync();
        var change = await ChangeAsync("Add CSV export", ChangeStatus.InDevelopment);
        var item = await ClaimAsync(change, WorkOrderItemStatus.Reported);

        await Transition(change.Id, ChangeStatus.Rejected);

        await _db.Entry(item).ReloadAsync();
        Assert.Equal(WorkOrderItemStatus.Reported, item.Status);
    }

    [Fact]
    public async Task ListedChanges_CarryTheirLatestReportForTheBoardCard()
    {
        await SetupAsync();
        var change = await ChangeAsync("Add CSV export", ChangeStatus.InDevelopment);
        await ClaimAsync(change, WorkOrderItemStatus.Reported);

        var listed = Assert.Single(await _changes.ListAsync(_projectId, ChangeStatus.InDevelopment));

        Assert.Equal(WorkOrderItemStatus.Reported, listed.WorkOrderStatus);
        Assert.NotNull(listed.WorkOrderReportedAt);
    }

    [Fact]
    public async Task ListedChanges_TakeTheLatestReportWhenAChangeWasReexported()
    {
        await SetupAsync();
        var change = await ChangeAsync("Add CSV export", ChangeStatus.InDevelopment);
        var first = await ClaimAsync(change, WorkOrderItemStatus.Reported);
        first.ReportedAt = DateTimeOffset.UtcNow.AddHours(-2);
        await ClaimAsync(change, WorkOrderItemStatus.Blocked);
        await _db.SaveChangesAsync();

        var listed = Assert.Single(await _changes.ListAsync(_projectId, ChangeStatus.InDevelopment));

        // Re-export leaves an item in each order; the most recent word wins.
        Assert.Equal(WorkOrderItemStatus.Blocked, listed.WorkOrderStatus);
    }

    [Fact]
    public async Task ListedChanges_CarryNoReportWhenNothingWasEverExported()
    {
        await SetupAsync();
        await ChangeAsync("Add CSV export", ChangeStatus.InDevelopment);

        var listed = Assert.Single(await _changes.ListAsync(_projectId, ChangeStatus.InDevelopment));

        Assert.Null(listed.WorkOrderStatus);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
