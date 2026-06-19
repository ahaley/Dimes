using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dimes.Api.Auth;

/// <summary>Seeds the bootstrap site administrator from config on startup so a fresh install has a
/// way in. Idempotent: safe to run every boot. In local mode it also ensures the admin has a
/// password credential (hashed from <c>Auth:SiteAdmin:InitialPassword</c>) if one is missing.</summary>
public sealed class AuthBootstrapper(
    DimesDbContext db, IOptions<AuthOptions> options, IPasswordHasher<Actor> hasher)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var cfg = options.Value;
        var email = cfg.SiteAdmin.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
        {
            return;
        }

        var actor = await db.Actors.FirstOrDefaultAsync(a => a.Email != null && a.Email.ToLower() == email, ct);
        if (actor is null)
        {
            actor = new Actor { DisplayName = email, Type = ActorType.Human, Email = email };
            db.Actors.Add(actor);
        }
        actor.IsSiteAdmin = true;

        if (cfg.Mode == AuthMode.Local && !string.IsNullOrWhiteSpace(cfg.SiteAdmin.InitialPassword))
        {
            var hasCredential = await db.LocalCredentials.AnyAsync(c => c.ActorId == actor.Id, ct);
            if (!hasCredential)
            {
                db.LocalCredentials.Add(new LocalCredential
                {
                    ActorId = actor.Id,
                    PasswordHash = hasher.HashPassword(actor, cfg.SiteAdmin.InitialPassword),
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
