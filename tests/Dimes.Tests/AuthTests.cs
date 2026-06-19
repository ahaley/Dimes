using System.Security.Claims;
using Dimes.Api;
using Dimes.Api.Auth;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dimes.Tests;

/// <summary>Covers the auth building blocks in isolation (no HTTP pipeline): password hashing, OIDC
/// JIT provisioning, config-seeded site-admin bootstrap, and current-actor claim resolution.</summary>
public sealed class AuthTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly PasswordHasher<Actor> _hasher = new();

    public AuthTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();
    }

    [Fact]
    public void PasswordHasher_RoundTrips_AndRejectsWrongPassword()
    {
        var actor = new Actor { DisplayName = "Maud", Type = ActorType.Human, Email = "maud@x.com" };
        var hash = _hasher.HashPassword(actor, "s3cret!");

        Assert.NotEqual("s3cret!", hash);
        Assert.Equal(PasswordVerificationResult.Success, _hasher.VerifyHashedPassword(actor, hash, "s3cret!"));
        Assert.Equal(PasswordVerificationResult.Failed, _hasher.VerifyHashedPassword(actor, hash, "wrong"));
    }

    [Fact]
    public async Task Jit_CreatesActorWithoutMembership_ThenReusesByEmail()
    {
        var first = await JitProvisioning.ProvisionAsync(_db, "New.User@X.com", "New User");

        Assert.Equal(ActorType.Human, first.Type);
        Assert.Equal("new.user@x.com", first.Email); // normalized
        Assert.False(await _db.Memberships.AnyAsync(m => m.ActorId == first.Id)); // no project access

        // Same identity (different casing) reuses the actor rather than duplicating.
        var second = await JitProvisioning.ProvisionAsync(_db, "new.user@x.com", "New User");
        Assert.Equal(first.Id, second.Id);
        Assert.Single(await _db.Actors.Where(a => a.Email == "new.user@x.com").ToListAsync());
    }

    [Fact]
    public async Task Bootstrapper_SeedsSiteAdminWithCredential_AndIsIdempotent()
    {
        var options = Options.Create(new AuthOptions
        {
            Mode = AuthMode.Local,
            SiteAdmin = new SiteAdminOptions { Email = "Admin@X.com", InitialPassword = "letmein" },
        });
        var bootstrapper = new AuthBootstrapper(_db, options, _hasher);

        await bootstrapper.SeedAsync();
        await bootstrapper.SeedAsync(); // run twice — must not duplicate.

        var admin = Assert.Single(await _db.Actors.Where(a => a.Email == "admin@x.com").ToListAsync());
        Assert.True(admin.IsSiteAdmin);
        var creds = await _db.LocalCredentials.Where(c => c.ActorId == admin.Id).ToListAsync();
        var credential = Assert.Single(creds);
        Assert.Equal(PasswordVerificationResult.Success, _hasher.VerifyHashedPassword(admin, credential.PasswordHash, "letmein"));
    }

    [Fact]
    public async Task Bootstrapper_PromotesExistingActor()
    {
        _db.Actors.Add(new Actor { DisplayName = "Existing", Type = ActorType.Human, Email = "boss@x.com" });
        await _db.SaveChangesAsync();

        var options = Options.Create(new AuthOptions
        {
            Mode = AuthMode.Oidc, // no local credential expected in OIDC mode
            SiteAdmin = new SiteAdminOptions { Email = "boss@x.com" },
        });
        await new AuthBootstrapper(_db, options, _hasher).SeedAsync();

        var boss = Assert.Single(await _db.Actors.Where(a => a.Email == "boss@x.com").ToListAsync());
        Assert.True(boss.IsSiteAdmin);
        Assert.False(await _db.LocalCredentials.AnyAsync(c => c.ActorId == boss.Id));
    }

    [Fact]
    public async Task CurrentActor_ResolvesFromClaim_AndThrowsWhenAbsent()
    {
        var actor = new Actor { DisplayName = "Cory", Type = ActorType.Human, Email = "cory@x.com", IsSiteAdmin = true };
        _db.Actors.Add(actor);
        await _db.SaveChangesAsync();

        var resolved = new CurrentActor(AccessorWith(actor.Id, isSiteAdmin: true), _db);
        Assert.True(resolved.IsAuthenticated);
        Assert.Equal(actor.Id, resolved.ActorId);
        Assert.True(resolved.IsSiteAdmin);
        Assert.Equal(actor.Id, (await resolved.GetAsync()).Id);

        var anonymous = new CurrentActor(new HttpContextAccessor { HttpContext = new DefaultHttpContext() }, _db);
        Assert.False(anonymous.IsAuthenticated);
        Assert.Throws<UnauthorizedException>(() => anonymous.ActorId);
    }

    private static IHttpContextAccessor AccessorWith(Guid actorId, bool isSiteAdmin)
    {
        var claims = new List<Claim> { new(DimesClaims.ActorId, actorId.ToString()) };
        if (isSiteAdmin)
        {
            claims.Add(new Claim(DimesClaims.SiteAdmin, "true"));
        }
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthSchemes.Cookie));
        return new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
