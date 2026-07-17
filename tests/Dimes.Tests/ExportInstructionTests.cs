using Dimes.Api;
using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Domain.Lifecycle;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>The In-Development export reads its work-order guidance from the project's editable
/// ExportWorkOrder instruction, falling back to the built-in default when no row exists. The generated
/// title and "## Changes" scaffolding stay code-side regardless.</summary>
public sealed class ExportInstructionTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ChangeRequestService _changes;

    public ExportInstructionTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();

        var resolver = new MembershipResolver(_db);
        _changes = new ChangeRequestService(_db, new LifecycleService(), resolver, new FakeBoardNotifier(), new NotificationDispatcher(_db));
    }

    // Add a project directly, with no seeded instruction row, so each test controls whether an override
    // exists. Exporting mints a work order under the exporting actor's authority, so give it one member.
    private async Task<(Project Project, Guid ActorId)> AddProjectAsync(string name)
    {
        var project = new Project { Name = name };
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
        return (project, actor.Id);
    }

    // Put a change In Development directly: the export only mints a work order (and so only renders the
    // report-back section) when there's something to work on.
    private async Task AddInDevelopmentChangeAsync(Project project, Guid actorId, string title)
    {
        _db.ChangeRequests.Add(new ChangeRequest
        {
            ProjectId = project.Id,
            Title = title,
            Status = ChangeStatus.InDevelopment,
            CreatedByActorId = actorId,
        });
        await _db.SaveChangesAsync();
    }

    private static string Lf(string s) => s.Replace("\r\n", "\n");

    [Fact]
    public async Task Export_WithNoInstructionRow_UsesTheBuiltInDefault()
    {
        var (project, actorId) = await AddProjectAsync("Demo");

        var export = await _changes.ExportInDevelopmentAsync(project.Id, actorId, "https://dimes.test");
        var md = Lf(export.Markdown);

        Assert.Contains("# Work order — implement In-Development changes (Demo)", md);
        Assert.Contains(Lf(SystemInstructionDefaults.ExportWorkOrder), md);
        Assert.Contains("## Changes", md);
    }

    [Fact]
    public async Task Export_WithCustomInstructionRow_UsesTheCustomGuidance()
    {
        var (project, actorId) = await AddProjectAsync("Demo");
        await AddInDevelopmentChangeAsync(project, actorId, "Add CSV export");
        _db.SystemInstructions.Add(new SystemInstruction
        {
            ProjectId = project.Id,
            Kind = SystemInstructionKind.ExportWorkOrder,
            Content = "## Custom\n\nDo it my way.",
        });
        await _db.SaveChangesAsync();

        var export = await _changes.ExportInDevelopmentAsync(project.Id, actorId, "https://dimes.test");
        var md = Lf(export.Markdown);

        // Custom guidance replaces the default, but the generated scaffolding remains.
        Assert.Contains("Do it my way.", md);
        Assert.DoesNotContain("Work autonomously through the whole list", md);
        Assert.Contains("# Work order — implement In-Development changes (Demo)", md);
        Assert.Contains("## Changes", md);
        // The report-back contract is renderer-owned, not part of the editable guidance — so a project that
        // has customized (or never updated) its instruction still gets a working round-trip. This is the
        // whole reason the section doesn't live in SystemInstructionDefaults: the bootstrapper seeds each
        // project its own copy, so a change to the default would never reach an existing project.
        Assert.Contains("## Report back", md);
    }

    [Fact]
    public async Task Export_WithNothingInDevelopment_MintsNoWorkOrderAndNoToken()
    {
        var (project, actorId) = await AddProjectAsync("Demo");

        var export = await _changes.ExportInDevelopmentAsync(project.Id, actorId, "https://dimes.test");

        // Nothing to report on: a capability token with no items would be pure liability.
        Assert.DoesNotContain("## Report back", Lf(export.Markdown));
        Assert.Empty(await _db.WorkOrders.ToListAsync());
    }

    [Fact]
    public async Task Export_ByNonMember_IsForbidden()
    {
        var (project, memberId) = await AddProjectAsync("Demo");
        await AddInDevelopmentChangeAsync(project, memberId, "Add CSV export");
        var stranger = new Actor { DisplayName = "Stranger", Type = ActorType.Human };
        _db.Actors.Add(stranger);
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ForbiddenException>(
            () => _changes.ExportInDevelopmentAsync(project.Id, stranger.Id, "https://dimes.test"));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
