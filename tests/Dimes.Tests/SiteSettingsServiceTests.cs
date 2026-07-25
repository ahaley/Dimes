using Dimes.Api;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain.Entities;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Dimes.Tests;

/// <summary>The single-row site settings: branding title and the project-creation policy. The row is
/// created lazily on first write, so every read has to fall back to the built-in defaults.</summary>
public sealed class SiteSettingsServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly SiteSettingsService _settings;

    public SiteSettingsServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();

        _settings = new SiteSettingsService(_db);
    }

    [Fact]
    public async Task Reads_FallBackToDefaults_WhenNoRowExists()
    {
        Assert.Empty(await _db.SiteSettings.ToListAsync());

        Assert.Equal(SiteSettings.DefaultTitle, (await _settings.GetAsync()).Title);
        Assert.Equal(SiteSettings.DefaultProjectLimit, (await _settings.GetProjectPolicyAsync()).ProjectLimit);
    }

    [Fact]
    public async Task ProjectPolicy_RoundTrips_AndKeepsToOneRow()
    {
        await _settings.UpdateProjectPolicyAsync(new UpdateProjectPolicyRequest(10));
        Assert.Equal(10, (await _settings.GetProjectPolicyAsync()).ProjectLimit);

        await _settings.UpdateProjectPolicyAsync(new UpdateProjectPolicyRequest(0));
        Assert.Equal(0, (await _settings.GetProjectPolicyAsync()).ProjectLimit);

        Assert.Single(await _db.SiteSettings.ToListAsync());
    }

    /// <summary>The two settings share one row, so writing either must not clobber the other's value —
    /// including the defaults a lazily-created row starts with.</summary>
    [Fact]
    public async Task BrandingAndProjectPolicy_DoNotClobberEachOther()
    {
        await _settings.UpdateProjectPolicyAsync(new UpdateProjectPolicyRequest(7));
        await _settings.UpdateAsync(new UpdateSiteBrandingRequest("Acme Tracker"));

        Assert.Equal("Acme Tracker", (await _settings.GetAsync()).Title);
        Assert.Equal(7, (await _settings.GetProjectPolicyAsync()).ProjectLimit);
    }

    [Fact]
    public async Task BrandingFirst_LeavesTheProjectLimitAtItsDefault()
    {
        await _settings.UpdateAsync(new UpdateSiteBrandingRequest("Acme Tracker"));

        Assert.Equal(SiteSettings.DefaultProjectLimit, (await _settings.GetProjectPolicyAsync()).ProjectLimit);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(SiteSettings.MaxProjectLimit + 1)]
    public async Task ProjectLimit_OutOfRange_IsRejected(int limit)
    {
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _settings.UpdateProjectPolicyAsync(new UpdateProjectPolicyRequest(limit)));
    }

    /// <summary>Upgrade path: an install that already saved site settings must come out of the
    /// AddProjectQuota migration allowing 3 projects, not 0. EF backfills a new non-nullable int with the
    /// type default, and 0 here means "non-admins can't create projects" — so that migration hand-edits
    /// <c>defaultValue</c> to match <see cref="SiteSettings.DefaultProjectLimit"/>. This test is what keeps
    /// the two in step: a regenerated migration would silently disable creation on every upgraded install.</summary>
    [Fact]
    public async Task ExistingSiteSettingsRow_IsBackfilledWithTheDefaultProjectLimit()
    {
        using var connection = new SqliteConnection("Filename=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(connection).Options;
        using var db = new DimesDbContext(options);

        // Stop at the migration immediately before quotas, so the SiteSettings table has no limit column.
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
        await migrator.MigrateAsync("AddNotifications");

        // An install that had customized its branding — i.e. the row already exists.
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO SiteSettings (Id, Title, CreatedAt, UpdatedAt) VALUES ({0}, {1}, 0, 0)",
            Guid.NewGuid().ToString(), "Acme Tracker");

        await migrator.MigrateAsync();

        var limit = await db.SiteSettings.AsNoTracking().Select(s => s.ProjectLimit).SingleAsync();
        Assert.Equal(SiteSettings.DefaultProjectLimit, limit);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
