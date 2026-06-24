using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>The startup seeder gives every project an editable ExportWorkOrder instruction seeded from the
/// built-in default — idempotently, and without clobbering a row a user has customized.</summary>
public sealed class SystemInstructionBootstrapperTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly SystemInstructionBootstrapper _seeder;

    public SystemInstructionBootstrapperTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();
        _seeder = new SystemInstructionBootstrapper(_db);
    }

    private async Task<Project> AddProjectAsync(string name)
    {
        var project = new Project { Name = name };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        return project;
    }

    [Fact]
    public async Task Seed_GivesEveryProjectTheDefaultExportInstruction()
    {
        await AddProjectAsync("A");
        await AddProjectAsync("B");

        await _seeder.SeedAsync();

        var rows = await _db.SystemInstructions.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal(SystemInstructionKind.ExportWorkOrder, r.Kind);
            Assert.Equal(SystemInstructionDefaults.ExportWorkOrder, r.Content);
        });
    }

    [Fact]
    public async Task Seed_IsIdempotent_NoDuplicatesOnRerun()
    {
        await AddProjectAsync("A");

        await _seeder.SeedAsync();
        await _seeder.SeedAsync();
        await _seeder.SeedAsync();

        Assert.Equal(1, await _db.SystemInstructions.CountAsync());
    }

    [Fact]
    public async Task Seed_DoesNotOverwriteACustomizedInstruction()
    {
        var project = await AddProjectAsync("A");
        _db.SystemInstructions.Add(new SystemInstruction
        {
            ProjectId = project.Id,
            Kind = SystemInstructionKind.ExportWorkOrder,
            Content = "custom guidance",
        });
        await _db.SaveChangesAsync();

        await _seeder.SeedAsync();

        var row = Assert.Single(await _db.SystemInstructions.ToListAsync());
        Assert.Equal("custom guidance", row.Content);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
