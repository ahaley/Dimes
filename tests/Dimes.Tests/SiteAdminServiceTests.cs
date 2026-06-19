using Dimes.Api;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>Site-admin user management: edit (email uniqueness), archive, delete (removes the login
/// credential; blocked when referenced), and the last-site-admin lockout guard.</summary>
public sealed class SiteAdminServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ProjectService _projects;
    private readonly SiteAdminService _admin;

    public SiteAdminServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();

        _projects = new ProjectService(_db);
        _admin = new SiteAdminService(_db, new PasswordHasher<Actor>(), _projects);
    }

    private Task<SiteUserDto> CreateUser(string name, string email, bool admin = false) =>
        _admin.CreateLocalUserAsync(new CreateLocalUserRequest(name, email, "pw-" + name, admin));

    [Fact]
    public async Task UpdateUser_ChangesEmail_NormalizedAndUnique()
    {
        var a = await CreateUser("Ann", "ann@x.com");
        await CreateUser("Bob", "bob@x.com");

        var updated = await _admin.UpdateUserAsync(a.Id, new UpdateActorRequest("Ann R", "Ann.New@X.com"));
        Assert.Equal("Ann R", updated.DisplayName);
        Assert.Equal("ann.new@x.com", updated.Email); // normalized

        // Colliding with Bob's email is rejected.
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _admin.UpdateUserAsync(a.Id, new UpdateActorRequest("Ann R", "bob@x.com")));
    }

    [Fact]
    public async Task ArchiveUser_TogglesArchivedFlag()
    {
        var u = await CreateUser("Cara", "cara@x.com");

        await _admin.ArchiveUserAsync(u.Id, archived: true);
        Assert.True((await _db.Actors.FindAsync(u.Id))!.IsArchived);

        await _admin.ArchiveUserAsync(u.Id, archived: false);
        Assert.False((await _db.Actors.FindAsync(u.Id))!.IsArchived);
    }

    [Fact]
    public async Task DeleteUser_RemovesCredential_WhenUnreferenced()
    {
        var u = await CreateUser("Dan", "dan@x.com");
        Assert.True(await _db.LocalCredentials.AnyAsync(c => c.ActorId == u.Id));

        await _admin.DeleteUserAsync(u.Id);

        Assert.False(await _db.Actors.AnyAsync(a => a.Id == u.Id));
        Assert.False(await _db.LocalCredentials.AnyAsync(c => c.ActorId == u.Id));
    }

    [Fact]
    public async Task DeleteUser_BlockedWhenReferenced()
    {
        var u = await CreateUser("Eve", "eve@x.com");
        // A breadcrumb: an audit event authored by the user.
        _db.AuditEvents.Add(new AuditEvent
        {
            EntityType = AuditEntityType.ChangeRequest,
            EntityId = Guid.NewGuid(),
            ActorId = u.Id,
            Action = "Created",
        });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<BadRequestException>(() => _admin.DeleteUserAsync(u.Id));
        Assert.True(await _db.Actors.AnyAsync(a => a.Id == u.Id)); // still there → archive instead
    }

    [Fact]
    public async Task LastSiteAdmin_CannotBeDemotedArchivedOrDeleted()
    {
        var admin = await CreateUser("Root", "root@x.com", admin: true);

        await Assert.ThrowsAsync<BadRequestException>(() => _admin.SetSiteAdminAsync(admin.Id, false));
        await Assert.ThrowsAsync<BadRequestException>(() => _admin.ArchiveUserAsync(admin.Id, archived: true));
        await Assert.ThrowsAsync<BadRequestException>(() => _admin.DeleteUserAsync(admin.Id));

        // With a second effective admin, the guard relaxes.
        await CreateUser("Root2", "root2@x.com", admin: true);
        var demoted = await _admin.SetSiteAdminAsync(admin.Id, false);
        Assert.False(demoted.IsSiteAdmin);
    }

    [Fact]
    public async Task ArchivedAdmin_DoesNotCountAsEffective_ForLastAdminGuard()
    {
        var first = await CreateUser("A1", "a1@x.com", admin: true);
        var second = await CreateUser("A2", "a2@x.com", admin: true);
        await _admin.ArchiveUserAsync(second.Id, archived: true); // archived → not effective

        // first is now the only EFFECTIVE admin, so demoting it must be blocked.
        await Assert.ThrowsAsync<BadRequestException>(() => _admin.SetSiteAdminAsync(first.Id, false));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
