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

        _projects = new ProjectService(_db, new MembershipResolver(_db));
        _admin = new SiteAdminService(_db, new PasswordHasher<Actor>(), _projects);
    }

    private Task<SiteUserDto> CreateUser(string name, string email, bool admin = false) =>
        _admin.CreateLocalUserAsync(new CreateLocalUserRequest(name, email, "pw-" + name, admin));

    [Fact]
    public async Task UpdateUser_ChangesEmail_NormalizedAndUnique()
    {
        var a = await CreateUser("Ann", "ann@x.com");
        await CreateUser("Bob", "bob@x.com");

        var updated = await _admin.UpdateUserAsync(a.Id, new UpdateActorRequest("Ann R", "Ann.New@X.com"), Ct);
        Assert.Equal("Ann R", updated.DisplayName);
        Assert.Equal("ann.new@x.com", updated.Email); // normalized

        // Colliding with Bob's email is rejected.
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _admin.UpdateUserAsync(a.Id, new UpdateActorRequest("Ann R", "bob@x.com"), Ct));
    }

    [Fact]
    public async Task ArchiveUser_TogglesArchivedFlag()
    {
        var u = await CreateUser("Cara", "cara@x.com");

        await _admin.ArchiveUserAsync(u.Id, archived: true, ct: Ct);
        Assert.True((await _db.Actors.FindAsync(new object?[] { u.Id }, Ct))!.IsArchived);

        await _admin.ArchiveUserAsync(u.Id, archived: false, ct: Ct);
        Assert.False((await _db.Actors.FindAsync(new object?[] { u.Id }, Ct))!.IsArchived);
    }

    [Fact]
    public async Task DeleteUser_RemovesCredential_WhenUnreferenced()
    {
        var u = await CreateUser("Dan", "dan@x.com");
        Assert.True(await _db.LocalCredentials.AnyAsync(c => c.ActorId == u.Id, cancellationToken: Ct));

        await _admin.DeleteUserAsync(u.Id, Ct);

        Assert.False(await _db.Actors.AnyAsync(a => a.Id == u.Id, cancellationToken: Ct));
        Assert.False(await _db.LocalCredentials.AnyAsync(c => c.ActorId == u.Id, cancellationToken: Ct));
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
        await _db.SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<BadRequestException>(() => _admin.DeleteUserAsync(u.Id, Ct));
        Assert.True(await _db.Actors.AnyAsync(a => a.Id == u.Id, cancellationToken: Ct)); // still there → archive instead
    }

    [Fact]
    public async Task LastSiteAdmin_CannotBeDemotedArchivedOrDeleted()
    {
        var admin = await CreateUser("Root", "root@x.com", admin: true);

        await Assert.ThrowsAsync<BadRequestException>(() => _admin.SetSiteAdminAsync(admin.Id, false, Ct));
        await Assert.ThrowsAsync<BadRequestException>(() => _admin.ArchiveUserAsync(admin.Id, archived: true, ct: Ct));
        await Assert.ThrowsAsync<BadRequestException>(() => _admin.DeleteUserAsync(admin.Id, Ct));

        // With a second effective admin, the guard relaxes.
        await CreateUser("Root2", "root2@x.com", admin: true);
        var demoted = await _admin.SetSiteAdminAsync(admin.Id, false, Ct);
        Assert.False(demoted.IsSiteAdmin);
    }

    [Fact]
    public async Task ArchivedAdmin_DoesNotCountAsEffective_ForLastAdminGuard()
    {
        var first = await CreateUser("A1", "a1@x.com", admin: true);
        var second = await CreateUser("A2", "a2@x.com", admin: true);
        await _admin.ArchiveUserAsync(second.Id, archived: true, ct: Ct); // archived → not effective

        // first is now the only EFFECTIVE admin, so demoting it must be blocked.
        await Assert.ThrowsAsync<BadRequestException>(() => _admin.SetSiteAdminAsync(first.Id, false, Ct));
    }

    [Fact]
    public async Task AssignMember_LinksExistingActor_AndUpsertsRole()
    {
        var project = await _projects.CreateAsync(_db, new CreateProjectRequest("P", null), ct: Ct);
        var user = await CreateUser("Mem", "mem@x.com");

        await _projects.AssignMemberAsync(project.Id, user.Id, MemberRole.Reporter, Ct);
        var first = Assert.Single(await _projects.ListMembersAsync(project.Id, Ct));
        Assert.Equal(user.Id, first.ActorId);
        Assert.Equal(MemberRole.Reporter, first.Role);

        // Re-assign → role updated in place, no duplicate membership (linking, not minting).
        await _projects.AssignMemberAsync(project.Id, user.Id, MemberRole.Maintainer, Ct);
        var again = Assert.Single(await _projects.ListMembersAsync(project.Id, Ct));
        Assert.Equal(MemberRole.Maintainer, again.Role);
        Assert.Single(await _db.Actors.Where(a => a.Email == "mem@x.com").ToListAsync(cancellationToken: Ct)); // still one actor
    }

    [Fact]
    public async Task UserMemberships_AssignAndRemove_RoundTrip()
    {
        var project = await _projects.CreateAsync(_db, new CreateProjectRequest("P", null), ct: Ct);
        var user = await CreateUser("Mem", "mem@x.com");

        await _admin.AssignMembershipAsync(user.Id, new AssignMembershipRequest(project.Id, MemberRole.Contributor), Ct);
        var m = Assert.Single(await _admin.ListUserMembershipsAsync(user.Id, Ct));
        Assert.Equal(project.Id, m.ProjectId);
        Assert.Equal("P", m.ProjectName);
        Assert.Equal(MemberRole.Contributor, m.Role);
        Assert.Contains(await _projects.ListMembersAsync(project.Id, Ct), x => x.ActorId == user.Id);

        await _admin.RemoveMembershipAsync(user.Id, project.Id, Ct);
        Assert.Empty(await _admin.ListUserMembershipsAsync(user.Id, Ct));
    }

    [Fact]
    public async Task CreateUser_WithoutPassword_HasNoCredential()
    {
        var user = await _admin.CreateLocalUserAsync(new CreateLocalUserRequest("NoPass", "nopass@x.com", null, false), Ct);

        Assert.False(user.HasLocalCredential);
        Assert.False(await _db.LocalCredentials.AnyAsync(c => c.ActorId == user.Id, cancellationToken: Ct));
    }

    [Fact]
    public async Task ProjectList_NonAdminSeesOnlyMemberProjects_AdminSeesAll()
    {
        var a = await _projects.CreateAsync(_db, new CreateProjectRequest("A", null), ct: Ct);
        await _projects.CreateAsync(_db, new CreateProjectRequest("B", null), ct: Ct);
        var ned = await CreateUser("Ned", "ned@x.com");
        var boss = await CreateUser("Boss", "boss@x.com", admin: true);
        await _projects.AssignMemberAsync(a.Id, ned.Id, MemberRole.Contributor, Ct);

        var nedSees = await _projects.ListAsync(ned.Id, isSiteAdmin: false, ct: Ct);
        Assert.Equal(a.Id, Assert.Single(nedSees).Id);

        Assert.Equal(2, (await _projects.ListAsync(boss.Id, isSiteAdmin: true, ct: Ct)).Count);
    }

    /// <summary>Every listed project carries the caller's own role in it, so a client can gate an
    /// affordance (e.g. the freestyle redirect targets) on their authority in a project other than the
    /// one they're viewing. A site admin's non-member project reports no role.</summary>
    [Fact]
    public async Task ProjectList_CarriesCallersRole_NullWhereNotAMember()
    {
        var a = await _projects.CreateAsync(_db, new CreateProjectRequest("A", null), ct: Ct);
        var b = await _projects.CreateAsync(_db, new CreateProjectRequest("B", null), ct: Ct);
        var ned = await CreateUser("Ned", "ned@x.com");
        var boss = await CreateUser("Boss", "boss@x.com", admin: true);
        await _projects.AssignMemberAsync(a.Id, ned.Id, MemberRole.Reporter, Ct);
        await _projects.AssignMemberAsync(b.Id, ned.Id, MemberRole.Contributor, Ct);
        await _projects.AssignMemberAsync(a.Id, boss.Id, MemberRole.Maintainer, Ct);

        var nedSees = await _projects.ListAsync(ned.Id, isSiteAdmin: false, ct: Ct);
        Assert.Equal(MemberRole.Reporter, nedSees.Single(p => p.Id == a.Id).MyRole);
        Assert.Equal(MemberRole.Contributor, nedSees.Single(p => p.Id == b.Id).MyRole);

        var bossSees = await _projects.ListAsync(boss.Id, isSiteAdmin: true, ct: Ct);
        Assert.Equal(MemberRole.Maintainer, bossSees.Single(p => p.Id == a.Id).MyRole);
        Assert.Null(bossSees.Single(p => p.Id == b.Id).MyRole); // visible as a site admin, but not a member
    }

    [Fact]
    public async Task ArchiveProject_HidesFromDefaultList_AndUnarchiveRestores()
    {
        var boss = await CreateUser("Boss", "boss@x.com", admin: true);
        var p = await _projects.CreateAsync(_db, new CreateProjectRequest("P", null), ct: Ct);

        await _projects.ArchiveProjectAsync(p.Id, archived: true, boss.Id, callerIsSiteAdmin: true, ct: Ct);
        Assert.True((await _db.Projects.FindAsync(new object?[] { p.Id }, Ct))!.IsArchived);
        Assert.Empty(await _projects.ListAsync(boss.Id, isSiteAdmin: true, ct: Ct)); // hidden by default
        Assert.Single(await _projects.ListAsync(boss.Id, isSiteAdmin: true, includeArchived: true, ct: Ct)); // opt-in shows it

        await _projects.ArchiveProjectAsync(p.Id, archived: false, boss.Id, callerIsSiteAdmin: true, ct: Ct);
        Assert.False((await _db.Projects.FindAsync(new object?[] { p.Id }, Ct))!.IsArchived);
        Assert.Single(await _projects.ListAsync(boss.Id, isSiteAdmin: true, ct: Ct));
    }

    [Fact]
    public async Task ArchiveProject_RequiresMaintainerOrSiteAdmin()
    {
        var p = await _projects.CreateAsync(_db, new CreateProjectRequest("P", null), ct: Ct);
        var contributor = await CreateUser("Con", "con@x.com");
        var maintainer = await CreateUser("Main", "main@x.com");
        await _projects.AssignMemberAsync(p.Id, contributor.Id, MemberRole.Contributor, Ct);
        await _projects.AssignMemberAsync(p.Id, maintainer.Id, MemberRole.Maintainer, Ct);

        // A Contributor (below Maintainer) cannot archive the project.
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _projects.ArchiveProjectAsync(p.Id, archived: true, contributor.Id, callerIsSiteAdmin: false, ct: Ct));

        // A project Maintainer can.
        await _projects.ArchiveProjectAsync(p.Id, archived: true, maintainer.Id, callerIsSiteAdmin: false, ct: Ct);
        Assert.True((await _db.Projects.FindAsync(new object?[] { p.Id }, Ct))!.IsArchived);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
