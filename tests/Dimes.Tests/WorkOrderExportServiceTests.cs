using System.Text.RegularExpressions;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Domain.Lifecycle;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>Exporting records the work order and mints the capability the agent reports back with. The
/// export and the ingest are two halves of one contract, so what the markdown says and what the row stores
/// have to agree exactly.</summary>
public sealed class WorkOrderExportServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ChangeRequestService _changes;

    private Guid _projectId;
    private Guid _actorId;
    private int _nextNumber = 1;

    public WorkOrderExportServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();

        var resolver = new MembershipResolver(_db);
        _changes = new ChangeRequestService(_db, new LifecycleService(), resolver, new FakeBoardNotifier());
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

    private async Task<ChangeRequest> InDevAsync(string title)
    {
        var change = new ChangeRequest
        {
            ProjectId = _projectId,
            Title = title,
            Status = ChangeStatus.InDevelopment,
            CreatedByActorId = _actorId,
            Number = _nextNumber++,
        };
        _db.ChangeRequests.Add(change);
        await _db.SaveChangesAsync();
        return change;
    }

    private Task<MarkdownExport> ExportAsync() =>
        _changes.ExportInDevelopmentAsync(_projectId, _actorId, "https://dimes.test");

    [Fact]
    public async Task Export_RecordsOneItemPerInDevelopmentChange()
    {
        await SetupAsync();
        await InDevAsync("Add CSV export");
        await InDevAsync("Fix login redirect");

        await ExportAsync();

        var workOrder = await _db.WorkOrders.Include(w => w.Items).SingleAsync();
        Assert.Equal(2, workOrder.Items.Count);
        Assert.Equal(_actorId, workOrder.ExportedByActorId);
        Assert.All(workOrder.Items, i => Assert.Equal(WorkOrderItemStatus.Pending, i.Status));
    }

    [Fact]
    public async Task Export_StoresTheSameBranchNameItPrintedInTheMarkdown()
    {
        await SetupAsync();
        await InDevAsync("Add CSV export");

        var export = await ExportAsync();

        // Branch matching is an equality check against a string we minted, so the rendered and stored
        // names must come from one place. If these ever drift, branch fallback silently rots.
        var item = await _db.WorkOrderItems.SingleAsync();
        Assert.Contains($"- Branch: `{item.BranchName}`", export.Markdown);
    }

    [Fact]
    public async Task Export_EmbedsTheReportBackContractWithTheTokenExactlyOnce()
    {
        await SetupAsync();
        await InDevAsync("Add CSV export");

        var export = await ExportAsync();

        Assert.Contains("## Report back", export.Markdown);
        Assert.Contains("Do not commit this file", export.Markdown);
        var tokens = Regex.Matches(export.Markdown, @"/api/work-orders/(?<token>[A-Za-z0-9_-]+)/results");
        Assert.Single(tokens);
        Assert.Contains("https://dimes.test/api/work-orders/", export.Markdown);
    }

    [Fact]
    public async Task Export_NeverPersistsTheRawToken()
    {
        await SetupAsync();
        await InDevAsync("Add CSV export");

        var export = await ExportAsync();

        var token = Regex.Match(export.Markdown, @"/api/work-orders/(?<token>[A-Za-z0-9_-]+)/results")
            .Groups["token"].Value;
        var workOrder = await _db.WorkOrders.SingleAsync();
        // Only the hash is stored, so a database read can't be replayed against the ingest endpoint.
        Assert.NotEqual(token, workOrder.TokenHash);
        Assert.Equal(WorkOrderToken.Hash(token), workOrder.TokenHash);
    }

    [Fact]
    public async Task Export_WithoutAnApiBaseUrl_EmitsThePathAndSaysSo()
    {
        await SetupAsync();
        await InDevAsync("Add CSV export");

        var export = await _changes.ExportInDevelopmentAsync(_projectId, _actorId, null);

        Assert.Contains("curl -X POST /api/work-orders/", export.Markdown);
        Assert.Contains("Prefix the path below with your Dimes origin", export.Markdown);
    }

    [Fact]
    public async Task ExportingTwice_MintsTwoIndependentWorkOrders()
    {
        await SetupAsync();
        await InDevAsync("Add CSV export");

        var first = await ExportAsync();
        var second = await ExportAsync();

        // Re-export doesn't supersede: the agent still holds the first file and may legitimately report
        // against it, so both tokens stay live.
        Assert.Equal(2, await _db.WorkOrders.CountAsync());
        Assert.Equal(2, await _db.WorkOrderItems.CountAsync());
        Assert.NotEqual(TokenOf(first.Markdown), TokenOf(second.Markdown));

        static string TokenOf(string md) =>
            Regex.Match(md, @"/api/work-orders/(?<token>[A-Za-z0-9_-]+)/results").Groups["token"].Value;
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
