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
        _changes = new ChangeRequestService(_db, new LifecycleService(), resolver, new FakeBoardNotifier());
    }

    // Add a project directly, with no seeded instruction row, so each test controls whether an override exists.
    private async Task<Project> AddProjectAsync(string name)
    {
        var project = new Project { Name = name };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        return project;
    }

    private static string Lf(string s) => s.Replace("\r\n", "\n");

    [Fact]
    public async Task Export_WithNoInstructionRow_UsesTheBuiltInDefault()
    {
        var project = await AddProjectAsync("Demo");

        var export = await _changes.ExportInDevelopmentAsync(project.Id);
        var md = Lf(export.Markdown);

        Assert.Contains("# Work order — implement In-Development changes (Demo)", md);
        Assert.Contains(Lf(SystemInstructionDefaults.ExportWorkOrder), md);
        Assert.Contains("## Changes", md);
    }

    [Fact]
    public async Task Export_WithCustomInstructionRow_UsesTheCustomGuidance()
    {
        var project = await AddProjectAsync("Demo");
        _db.SystemInstructions.Add(new SystemInstruction
        {
            ProjectId = project.Id,
            Kind = SystemInstructionKind.ExportWorkOrder,
            Content = "## Custom\n\nDo it my way.",
        });
        await _db.SaveChangesAsync();

        var export = await _changes.ExportInDevelopmentAsync(project.Id);
        var md = Lf(export.Markdown);

        // Custom guidance replaces the default, but the generated scaffolding remains.
        Assert.Contains("Do it my way.", md);
        Assert.DoesNotContain("Work autonomously through the whole list", md);
        Assert.Contains("# Work order — implement In-Development changes (Demo)", md);
        Assert.Contains("## Changes", md);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
