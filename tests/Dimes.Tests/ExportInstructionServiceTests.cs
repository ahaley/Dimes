using Dimes.Api;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>ProjectService read/edit of a project's export work-order guidance: default fallback, the
/// Maintainer/site-admin gate, upsert, and reset-to-default.</summary>
public sealed class ExportInstructionServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ProjectService _projects;

    public ExportInstructionServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();
        _projects = new ProjectService(_db, new MembershipResolver(_db));
    }

    private async Task<(Guid ProjectId, Guid MaintainerId, Guid ContributorId)> SeedProjectAsync()
    {
        var project = await _projects.CreateAsync(_db, new CreateProjectRequest("Demo", null));
        var maint = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Mae", ActorType.Human, "mae@x.com", MemberRole.Maintainer));
        var contrib = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Cory", ActorType.Human, "cory@x.com", MemberRole.Contributor));
        return (project.Id, maint.ActorId, contrib.ActorId);
    }

    [Fact]
    public async Task Create_SeedsTheDefaultExportInstruction()
    {
        var project = await _projects.CreateAsync(_db, new CreateProjectRequest("Fresh", null), ct: Ct);

        var row = Assert.Single(await _db.SystemInstructions.ToListAsync(cancellationToken: Ct));
        Assert.Equal(project.Id, row.ProjectId);
        Assert.Equal(SystemInstructionKind.ExportWorkOrder, row.Kind);
        Assert.Equal(SystemInstructionDefaults.ExportWorkOrder, row.Content);

        // It's a real per-project row, so the editor reports it as a customizable override, not the fallback.
        var dto = await _projects.GetExportInstructionAsync(project.Id, Ct);
        Assert.False(dto.IsDefault);
    }

    [Fact]
    public async Task Get_WithNoRow_FallsBackToBuiltInDefault()
    {
        // A project with no instruction row (predating the seed, or reset) falls back to the default.
        var project = new Project { Name = "Bare" };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync(Ct);

        var dto = await _projects.GetExportInstructionAsync(project.Id, Ct);

        Assert.True(dto.IsDefault);
        Assert.Equal(SystemInstructionDefaults.ExportWorkOrder, dto.Content);
    }

    [Fact]
    public async Task Update_ByMaintainer_UpsertsOverride_AndGetReturnsIt()
    {
        var (projectId, maintainerId, _) = await SeedProjectAsync();

        var saved = await _projects.UpdateExportInstructionAsync(
            projectId,
            new UpdateExportInstructionRequest("## Mine\n\nDo it my way."),
            maintainerId,
            callerIsSiteAdmin: false,
            ct: Ct);
        Assert.False(saved.IsDefault);
        Assert.Equal("## Mine\n\nDo it my way.", saved.Content);

        var fetched = await _projects.GetExportInstructionAsync(projectId, Ct);
        Assert.False(fetched.IsDefault);
        Assert.Equal("## Mine\n\nDo it my way.", fetched.Content);
        Assert.Equal(1, await _db.SystemInstructions.CountAsync(cancellationToken: Ct));
    }

    [Fact]
    public async Task Update_BelowMaintainer_IsForbidden_AndLeavesGuidanceUnchanged()
    {
        var (projectId, _, contributorId) = await SeedProjectAsync();

        await Assert.ThrowsAsync<ForbiddenException>(() => _projects.UpdateExportInstructionAsync(
            projectId,
            new UpdateExportInstructionRequest("nope"),
            contributorId,
            callerIsSiteAdmin: false,
            ct: Ct));

        var dto = await _projects.GetExportInstructionAsync(projectId, Ct);
        Assert.Equal(SystemInstructionDefaults.ExportWorkOrder, dto.Content);
    }

    [Fact]
    public async Task Update_WithBlankBody_ResetsToDefault_RemovingTheRow()
    {
        var (projectId, maintainerId, _) = await SeedProjectAsync();
        await _projects.UpdateExportInstructionAsync(
            projectId,
            new UpdateExportInstructionRequest("custom"),
            maintainerId,
            callerIsSiteAdmin: false,
            ct: Ct);

        var reset = await _projects.UpdateExportInstructionAsync(
            projectId,
            new UpdateExportInstructionRequest("   "),
            maintainerId,
            callerIsSiteAdmin: false,
            ct: Ct);

        Assert.True(reset.IsDefault);
        Assert.Equal(SystemInstructionDefaults.ExportWorkOrder, reset.Content);
        Assert.Equal(0, await _db.SystemInstructions.CountAsync(cancellationToken: Ct));
    }

    [Fact]
    public async Task Get_MissingProject_Throws()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => _projects.GetExportInstructionAsync(Guid.NewGuid(), Ct));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
