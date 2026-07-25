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
public class SiteAdminService(DimesDbContext db, IPasswordHasher<Actor> hasher, ProjectService projects)
{
    public async Task<IReadOnlyList<SiteUserDto>> ListUsersAsync(CancellationToken ct = default) =>
        await db.Actors
            .Where(a => a.Type == ActorType.Human)
            .OrderBy(a => a.DisplayName)
            .Select(a => new SiteUserDto(
                a.Id, a.DisplayName, a.Email, a.Type, a.IsSiteAdmin,
                db.LocalCredentials.Any(c => c.ActorId == a.Id),
                a.IsArchived,
                !db.Memberships.Any(m => m.ActorId == a.Id)
                    && !db.Projects.Any(p => p.CreatedByActorId == a.Id)
                    && !db.ChangeRequests.Any(c => c.CreatedByActorId == a.Id || c.AssigneeActorId == a.Id)
                    && !db.Comments.Any(c => c.AuthorActorId == a.Id)
                    && !db.AuditEvents.Any(e => e.ActorId == a.Id),
                a.ProjectLimit,
                db.Projects.Count(p => p.CreatedByActorId == a.Id)))
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
        // Password is optional — a user can be pre-provisioned (or OIDC-only) and given one later.
        if (!string.IsNullOrWhiteSpace(req.Password))
        {
            db.LocalCredentials.Add(new LocalCredential
            {
                ActorId = actor.Id,
                PasswordHash = hasher.HashPassword(actor, req.Password),
            });
        }
        await db.SaveChangesAsync(ct);

        return await ToDtoAsync(actor.Id, ct);
    }

    public async Task<IReadOnlyList<UserMembershipDto>> ListUserMembershipsAsync(Guid actorId, CancellationToken ct = default) =>
        await db.Memberships
            .Where(m => m.ActorId == actorId)
            .OrderBy(m => m.Project.Name)
            .Select(m => new UserMembershipDto(m.ProjectId, m.Project.Name, m.Role))
            .ToListAsync(ct);

    public Task AssignMembershipAsync(Guid actorId, AssignMembershipRequest req, CancellationToken ct = default)
        => projects.AssignMemberAsync(req.ProjectId, actorId, req.Role, ct);

    public Task RemoveMembershipAsync(Guid actorId, Guid projectId, CancellationToken ct = default)
        => projects.RemoveMemberAsync(projectId, actorId, ct);

    /// <summary>Edit a user's identity (display name + email). Delegates to ProjectService, which
    /// enforces email uniqueness — email is the login identity.</summary>
    public async Task<SiteUserDto> UpdateUserAsync(Guid id, UpdateActorRequest req, CancellationToken ct = default)
    {
        await projects.UpdateActorAsync(id, req, ct);
        return await ToDtoAsync(id, ct);
    }

    /// <summary>Archive/unarchive a user. Archived users keep their history but can't sign in.
    /// Guarded against archiving the last site admin (in ProjectService).</summary>
    public Task ArchiveUserAsync(Guid id, bool archived, CancellationToken ct = default)
        => projects.ArchiveActorAsync(id, archived, ct);

    /// <summary>Hard-delete a user. Blocked by ProjectService when they're referenced by history
    /// (use archive instead) or are the last site admin.</summary>
    public Task DeleteUserAsync(Guid id, CancellationToken ct = default)
        => projects.DeleteActorAsync(id, ct);

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

        // Don't let the last effective site admin demote themselves into a lockout.
        if (!isSiteAdmin)
        {
            await projects.EnsureNotLastSiteAdminAsync(actor, ct);
        }

        actor.IsSiteAdmin = isSiteAdmin;
        await db.SaveChangesAsync(ct);

        return await ToDtoAsync(actor.Id, ct);
    }

    /// <summary>Set a user's personal project-creation allowance, overriding the site default in either
    /// direction. Null clears the override so they inherit the site policy again. This is the out-of-band
    /// escalation path: a user who has hit their limit asks an administrator, who raises just their number.
    /// Lowering it below what they've already created doesn't remove projects — it only blocks new ones.</summary>
    public async Task<SiteUserDto> SetProjectLimitAsync(
        Guid actorId, int? limit, CancellationToken ct = default)
    {
        var actor = await db.Actors.FindAsync([actorId], ct)
            ?? throw new NotFoundException($"Actor '{actorId}' not found.");

        if (limit is int value)
        {
            SiteSettingsService.ValidateProjectLimit(value);
        }

        actor.ProjectLimit = limit;
        await db.SaveChangesAsync(ct);

        return await ToDtoAsync(actor.Id, ct);
    }

    private async Task<SiteUserDto> ToDtoAsync(Guid actorId, CancellationToken ct)
    {
        var actor = await db.Actors.FindAsync([actorId], ct)
            ?? throw new NotFoundException($"Actor '{actorId}' not found.");
        var hasCredential = await db.LocalCredentials.AnyAsync(c => c.ActorId == actorId, ct);
        var created = await db.Projects.CountAsync(p => p.CreatedByActorId == actorId, ct);
        var deletable =
            !await db.Memberships.AnyAsync(m => m.ActorId == actorId, ct)
            && created == 0
            && !await db.ChangeRequests.AnyAsync(c => c.CreatedByActorId == actorId || c.AssigneeActorId == actorId, ct)
            && !await db.Comments.AnyAsync(c => c.AuthorActorId == actorId, ct)
            && !await db.AuditEvents.AnyAsync(e => e.ActorId == actorId, ct);
        return new SiteUserDto(
            actor.Id, actor.DisplayName, actor.Email, actor.Type,
            actor.IsSiteAdmin, hasCredential, actor.IsArchived, deletable, actor.ProjectLimit, created);
    }
}
