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

/// <summary>The per-user project-creation quota that lets non-admins create their own projects without
/// letting anyone flood the instance: a site-wide default, an optional per-user override, and a creator
/// who lands as Maintainer of what they made. Site admins are exempt throughout.</summary>
public sealed class ProjectQuotaTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ProjectService _projects;
    private readonly SiteAdminService _admin;
    private readonly SiteSettingsService _settings;

    public ProjectQuotaTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();

        _projects = new ProjectService(_db, new MembershipResolver(_db));
        _admin = new SiteAdminService(_db, new PasswordHasher<Actor>(), _projects);
        _settings = new SiteSettingsService(_db);
    }

    private Task<SiteUserDto> CreateUser(string name, string email, bool admin = false) =>
        _admin.CreateLocalUserAsync(new CreateLocalUserRequest(name, email, "pw-" + name, admin));

    private Task<ProjectDto> CreateAs(Guid actorId, string name, bool isSiteAdmin = false) =>
        _projects.CreateAsync(new CreateProjectRequest(name, null), actorId, isSiteAdmin);

    [Fact]
    public async Task NonAdmin_CreatesProject_AndLandsAsMaintainer()
    {
        var ned = await CreateUser("Ned", "ned@x.com");

        var project = await CreateAs(ned.Id, "Ned's project");

        Assert.Equal(MemberRole.Maintainer, project.MyRole);
        var membership = await _db.Memberships.SingleAsync(m => m.ProjectId == project.Id, cancellationToken: Ct);
        Assert.Equal(ned.Id, membership.ActorId);
        Assert.Equal(MemberRole.Maintainer, membership.Role);
        Assert.Equal(ned.Id, (await _db.Projects.FindAsync(new object?[] { project.Id }, Ct))!.CreatedByActorId);
    }

    /// <summary>Provenance has to survive the trip to the client on every path that returns a project —
    /// create, list, and edit — since the navigation isn't loaded by default and a missed Include would
    /// silently blank the creator out.</summary>
    [Fact]
    public async Task CreatorName_IsCarried_OnCreateListAndUpdate()
    {
        var ned = await CreateUser("Ned", "ned@x.com");

        var created = await CreateAs(ned.Id, "Ned's project");
        Assert.Equal(ned.Id, created.CreatedByActorId);
        Assert.Equal("Ned", created.CreatedByDisplayName);

        var listed = (await _projects.ListAsync(ned.Id, isSiteAdmin: false, ct: Ct)).Single();
        Assert.Equal(ned.Id, listed.CreatedByActorId);
        Assert.Equal("Ned", listed.CreatedByDisplayName);

        var updated = await _projects.UpdateAsync(
            created.Id,
            new UpdateProjectRequest("Renamed", null, true, false),
            ned.Id,
            callerIsSiteAdmin: false,
            ct: Ct);
        Assert.Equal("Ned", updated.CreatedByDisplayName);
    }

    /// <summary>Projects created before attribution existed report no creator rather than failing.</summary>
    [Fact]
    public async Task ProjectWithoutACreator_ReportsNoProvenance()
    {
        var ned = await CreateUser("Ned", "ned@x.com");
        var created = await CreateAs(ned.Id, "Orphan");

        // Simulate a pre-attribution row.
        (await _db.Projects.FindAsync(new object?[] { created.Id }, Ct))!.CreatedByActorId = null;
        await _db.SaveChangesAsync(Ct);

        var listed = (await _projects.ListAsync(ned.Id, isSiteAdmin: false, ct: Ct)).Single();
        Assert.Null(listed.CreatedByActorId);
        Assert.Null(listed.CreatedByDisplayName);
    }

    [Fact]
    public async Task NonAdmin_IsBlockedAtTheDefaultLimit()
    {
        var ned = await CreateUser("Ned", "ned@x.com");

        for (var i = 0; i < SiteSettings.DefaultProjectLimit; i++)
        {
            await CreateAs(ned.Id, $"P{i}");
        }

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateAs(ned.Id, "One too many"));
    }

    /// <summary>The decision most likely to regress: archive is a soft delete, so if archived projects
    /// freed a slot a user could archive-and-recreate without bound.</summary>
    [Fact]
    public async Task ArchivedProjects_StillCountAgainstTheLimit()
    {
        var ned = await CreateUser("Ned", "ned@x.com");
        await _admin.SetProjectLimitAsync(ned.Id, 2, Ct);

        var first = await CreateAs(ned.Id, "A");
        await CreateAs(ned.Id, "B");

        // Ned is a Maintainer of his own project, so he can archive it without an admin.
        await _projects.ArchiveProjectAsync(first.Id, archived: true, ned.Id, callerIsSiteAdmin: false, ct: Ct);

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateAs(ned.Id, "C"));

        var quota = await _projects.GetProjectQuotaAsync(ned.Id, isSiteAdmin: false, ct: Ct);
        Assert.Equal(2, quota.Used);
        Assert.False(quota.CanCreate);
    }

    [Fact]
    public async Task PersonalOverride_BeatsTheSiteDefault_InBothDirections()
    {
        await _settings.UpdateProjectPolicyAsync(new UpdateProjectPolicyRequest(1), Ct);

        var generous = await CreateUser("Gen", "gen@x.com");
        await _admin.SetProjectLimitAsync(generous.Id, 3, Ct);
        for (var i = 0; i < 3; i++)
        {
            await CreateAs(generous.Id, $"G{i}");
        }
        await Assert.ThrowsAsync<ForbiddenException>(() => CreateAs(generous.Id, "G4"));

        // And downward: an override below the site default still binds.
        await _settings.UpdateProjectPolicyAsync(new UpdateProjectPolicyRequest(5), Ct);
        var limited = await CreateUser("Lim", "lim@x.com");
        await _admin.SetProjectLimitAsync(limited.Id, 1, Ct);
        await CreateAs(limited.Id, "L1");
        await Assert.ThrowsAsync<ForbiddenException>(() => CreateAs(limited.Id, "L2"));
    }

    [Fact]
    public async Task ClearingTheOverride_FallsBackToTheSiteDefault()
    {
        await _settings.UpdateProjectPolicyAsync(new UpdateProjectPolicyRequest(2), Ct);
        var ned = await CreateUser("Ned", "ned@x.com");
        await _admin.SetProjectLimitAsync(ned.Id, 0, Ct);

        await Assert.ThrowsAsync<ForbiddenException>(() => CreateAs(ned.Id, "Blocked"));

        var cleared = await _admin.SetProjectLimitAsync(ned.Id, null, Ct);
        Assert.Null(cleared.ProjectLimit);
        Assert.Equal(2, (await _projects.GetProjectQuotaAsync(ned.Id, isSiteAdmin: false, ct: Ct)).Limit);
        await CreateAs(ned.Id, "Now allowed");
    }

    /// <summary>A site default of 0 is the off switch — it restores the admin-only behaviour that
    /// predates quotas — but an individual grant still lets one person through.</summary>
    [Fact]
    public async Task SiteDefaultOfZero_BlocksNonAdmins_ButAnOverrideStillGrants()
    {
        await _settings.UpdateProjectPolicyAsync(new UpdateProjectPolicyRequest(0), Ct);

        var ned = await CreateUser("Ned", "ned@x.com");
        await Assert.ThrowsAsync<ForbiddenException>(() => CreateAs(ned.Id, "Nope"));
        Assert.False((await _projects.GetProjectQuotaAsync(ned.Id, isSiteAdmin: false, ct: Ct)).CanCreate);

        var trusted = await CreateUser("Tru", "tru@x.com");
        await _admin.SetProjectLimitAsync(trusted.Id, 1, Ct);
        await CreateAs(trusted.Id, "Granted");
    }

    /// <summary>A limit of 0 has two sources that need different remedies: the site policy (creation is
    /// reserved for admins here) versus an override singling this user out. Reporting a personal 0 as site
    /// policy would send the user to ask for the wrong thing, so the quota reports which it was and the
    /// refusal message follows suit.</summary>
    [Fact]
    public async Task ZeroLimit_DistinguishesSitePolicyFromAPersonalOverride()
    {
        // Site policy 0, no personal override.
        await _settings.UpdateProjectPolicyAsync(new UpdateProjectPolicyRequest(0), Ct);
        var inherits = await CreateUser("Inh", "inh@x.com");

        var siteQuota = await _projects.GetProjectQuotaAsync(inherits.Id, isSiteAdmin: false, ct: Ct);
        Assert.Equal(0, siteQuota.Limit);
        Assert.False(siteQuota.LimitIsPersonal);
        var siteError = await Assert.ThrowsAsync<ForbiddenException>(() => CreateAs(inherits.Id, "Nope"));
        Assert.Contains("restricted to site administrators", siteError.Message);

        // A generous site policy, but this user is pinned to 0.
        await _settings.UpdateProjectPolicyAsync(new UpdateProjectPolicyRequest(5), Ct);
        var singledOut = await CreateUser("Sng", "sng@x.com");
        await _admin.SetProjectLimitAsync(singledOut.Id, 0, Ct);

        var personalQuota = await _projects.GetProjectQuotaAsync(singledOut.Id, isSiteAdmin: false, ct: Ct);
        Assert.Equal(0, personalQuota.Limit);
        Assert.True(personalQuota.LimitIsPersonal);
        var personalError = await Assert.ThrowsAsync<ForbiddenException>(() => CreateAs(singledOut.Id, "Nope"));
        Assert.DoesNotContain("restricted to site administrators", personalError.Message);
        Assert.Contains("Your project limit is set to 0", personalError.Message);
    }

    /// <summary>The source is also reported for non-zero limits, so a client can tell an inherited
    /// allowance from one an administrator set deliberately.</summary>
    [Fact]
    public async Task Quota_ReportsWhetherTheLimitIsInheritedOrPersonal()
    {
        var ned = await CreateUser("Ned", "ned@x.com");
        Assert.False((await _projects.GetProjectQuotaAsync(ned.Id, isSiteAdmin: false, ct: Ct)).LimitIsPersonal);

        await _admin.SetProjectLimitAsync(ned.Id, 7, Ct);
        Assert.True((await _projects.GetProjectQuotaAsync(ned.Id, isSiteAdmin: false, ct: Ct)).LimitIsPersonal);

        await _admin.SetProjectLimitAsync(ned.Id, null, Ct);
        Assert.False((await _projects.GetProjectQuotaAsync(ned.Id, isSiteAdmin: false, ct: Ct)).LimitIsPersonal);
    }

    [Fact]
    public async Task SiteAdmin_IsExempt_AndGainsNoMembership()
    {
        await _settings.UpdateProjectPolicyAsync(new UpdateProjectPolicyRequest(0), Ct);
        var boss = await CreateUser("Boss", "boss@x.com", admin: true);

        for (var i = 0; i < 4; i++)
        {
            var created = await CreateAs(boss.Id, $"B{i}", isSiteAdmin: true);
            Assert.Null(created.MyRole);
        }

        Assert.Empty(await _db.Memberships.Where(m => m.ActorId == boss.Id).ToListAsync(cancellationToken: Ct));

        var quota = await _projects.GetProjectQuotaAsync(boss.Id, isSiteAdmin: true, ct: Ct);
        Assert.True(quota.Unlimited);
        Assert.True(quota.CanCreate);
        Assert.Equal(4, quota.Used);
    }

    /// <summary>Lowering someone's limit below what they already hold never deletes projects — it only
    /// stops new ones.</summary>
    [Fact]
    public async Task LoweringTheLimitBelowUsage_KeepsProjects_ButBlocksNewOnes()
    {
        var ned = await CreateUser("Ned", "ned@x.com");
        await CreateAs(ned.Id, "A");
        await CreateAs(ned.Id, "B");

        await _admin.SetProjectLimitAsync(ned.Id, 1, Ct);

        Assert.Equal(2, await _db.Projects.CountAsync(p => p.CreatedByActorId == ned.Id, cancellationToken: Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => CreateAs(ned.Id, "C"));
    }

    [Fact]
    public async Task Quota_ReportsUsageAgainstTheEffectiveLimit()
    {
        var ned = await CreateUser("Ned", "ned@x.com");

        var before = await _projects.GetProjectQuotaAsync(ned.Id, isSiteAdmin: false, ct: Ct);
        Assert.Equal(0, before.Used);
        Assert.Equal(SiteSettings.DefaultProjectLimit, before.Limit);
        Assert.True(before.CanCreate);
        Assert.False(before.Unlimited);

        await CreateAs(ned.Id, "A");

        var after = await _projects.GetProjectQuotaAsync(ned.Id, isSiteAdmin: false, ct: Ct);
        Assert.Equal(1, after.Used);
        Assert.True(after.CanCreate);
    }

    /// <summary>Projects reference their creator under a Restrict FK, so the delete path has to report it
    /// as a friendly refusal rather than letting SaveChanges throw.</summary>
    [Fact]
    public async Task ActorWhoCreatedAProject_IsNotDeletable()
    {
        var ned = await CreateUser("Ned", "ned@x.com");
        var project = await CreateAs(ned.Id, "A");

        // Drop the membership so only the creator reference remains to block deletion.
        await _projects.RemoveMemberAsync(project.Id, ned.Id, Ct);

        Assert.False((await _projects.GetActorAsync(ned.Id, Ct)).Deletable);
        Assert.False((await _admin.ListUsersAsync(Ct)).Single(u => u.Id == ned.Id).Deletable);
        await Assert.ThrowsAsync<BadRequestException>(() => _projects.DeleteActorAsync(ned.Id, Ct));
    }

    [Fact]
    public async Task AdminUserList_SurfacesTheOverrideAndUsage()
    {
        var ned = await CreateUser("Ned", "ned@x.com");
        await CreateAs(ned.Id, "A");

        var listed = (await _admin.ListUsersAsync(Ct)).Single(u => u.Id == ned.Id);
        Assert.Null(listed.ProjectLimit); // inheriting the site default
        Assert.Equal(1, listed.ProjectsCreated);

        var updated = await _admin.SetProjectLimitAsync(ned.Id, 7, Ct);
        Assert.Equal(7, updated.ProjectLimit);
        Assert.Equal(1, updated.ProjectsCreated);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(SiteSettings.MaxProjectLimit + 1)]
    public async Task OutOfRangeLimits_AreRejected(int limit)
    {
        var ned = await CreateUser("Ned", "ned@x.com");

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _settings.UpdateProjectPolicyAsync(new UpdateProjectPolicyRequest(limit), Ct));
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _admin.SetProjectLimitAsync(ned.Id, limit, Ct));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
