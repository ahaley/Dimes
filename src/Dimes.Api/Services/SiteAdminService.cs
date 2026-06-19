using Dimes.Api.Contracts;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Services;

/// <summary>App-level user administration for site admins: list human users, create local
/// (email + password) accounts, reset passwords, and grant/revoke site-admin. Project membership and
/// roles remain managed per-project via <see cref="ProjectService"/>.</summary>
public class SiteAdminService(DimesDbContext db, IPasswordHasher<Actor> hasher)
{
    public async Task<IReadOnlyList<SiteUserDto>> ListUsersAsync(CancellationToken ct = default) =>
        await db.Actors
            .Where(a => a.Type == ActorType.Human)
            .OrderBy(a => a.DisplayName)
            .Select(a => new SiteUserDto(
                a.Id, a.DisplayName, a.Email, a.Type, a.IsSiteAdmin,
                db.LocalCredentials.Any(c => c.ActorId == a.Id), a.IsArchived))
            .ToListAsync(ct);

    public async Task<SiteUserDto> CreateLocalUserAsync(CreateLocalUserRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.DisplayName))
        {
            throw new BadRequestException("Display name is required.");
        }
        var email = req.Email?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new BadRequestException("Email is required.");
        }
        if (string.IsNullOrWhiteSpace(req.Password))
        {
            throw new BadRequestException("Password is required.");
        }
        if (await db.Actors.AnyAsync(a => a.Email != null && a.Email.ToLower() == email, ct))
        {
            throw new BadRequestException("An actor with that email already exists.");
        }

        var actor = new Actor
        {
            DisplayName = req.DisplayName.Trim(),
            Type = ActorType.Human,
            Email = email,
            IsSiteAdmin = req.IsSiteAdmin,
        };
        db.Actors.Add(actor);
        db.LocalCredentials.Add(new LocalCredential
        {
            ActorId = actor.Id,
            PasswordHash = hasher.HashPassword(actor, req.Password),
        });
        await db.SaveChangesAsync(ct);

        return new SiteUserDto(actor.Id, actor.DisplayName, actor.Email, actor.Type, actor.IsSiteAdmin, true, actor.IsArchived);
    }

    public async Task ResetPasswordAsync(Guid actorId, ResetPasswordRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Password))
        {
            throw new BadRequestException("Password is required.");
        }

        var actor = await db.Actors.FindAsync([actorId], ct)
            ?? throw new NotFoundException($"Actor '{actorId}' not found.");

        var credential = await db.LocalCredentials.FirstOrDefaultAsync(c => c.ActorId == actorId, ct);
        if (credential is null)
        {
            db.LocalCredentials.Add(new LocalCredential
            {
                ActorId = actorId,
                PasswordHash = hasher.HashPassword(actor, req.Password),
            });
        }
        else
        {
            credential.PasswordHash = hasher.HashPassword(actor, req.Password);
            credential.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<SiteUserDto> SetSiteAdminAsync(Guid actorId, bool isSiteAdmin, CancellationToken ct = default)
    {
        var actor = await db.Actors.FindAsync([actorId], ct)
            ?? throw new NotFoundException($"Actor '{actorId}' not found.");

        actor.IsSiteAdmin = isSiteAdmin;
        await db.SaveChangesAsync(ct);

        var hasCredential = await db.LocalCredentials.AnyAsync(c => c.ActorId == actorId, ct);
        return new SiteUserDto(actor.Id, actor.DisplayName, actor.Email, actor.Type, actor.IsSiteAdmin, hasCredential, actor.IsArchived);
    }
}
