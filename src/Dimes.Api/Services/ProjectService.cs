using Dimes.Api.Contracts;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Services;

public class ProjectService(DimesDbContext db)
{
    public async Task<ProjectDto> CreateAsync(CreateProjectRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
        {
            throw new BadRequestException("Project name is required.");
        }

        var project = new Project { Name = req.Name.Trim(), Description = req.Description };
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return project.ToDto();
    }

    public async Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken ct = default) =>
        await db.Projects.OrderBy(p => p.Name).Select(p => p.ToDto()).ToListAsync(ct);

    /// <summary>Create an actor and bind them to the project with a role (pass-1 stand-in for an
    /// identity/invite flow).</summary>
    public async Task<MemberDto> AddMemberAsync(Guid projectId, AddMemberRequest req, CancellationToken ct = default)
    {
        var project = await db.Projects.FindAsync([projectId], ct)
            ?? throw new NotFoundException($"Project '{projectId}' not found.");

        if (string.IsNullOrWhiteSpace(req.DisplayName))
        {
            throw new BadRequestException("Member display name is required.");
        }

        if (req.LlmProviderConfigId is not null)
        {
            if (req.Type != ActorType.Agent)
            {
                throw new BadRequestException("Only Agent actors can be bound to an LLM provider config.");
            }

            var configExists = await db.LlmProviderConfigs.AnyAsync(c => c.Id == req.LlmProviderConfigId, ct);
            if (!configExists)
            {
                throw new NotFoundException($"LLM provider config '{req.LlmProviderConfigId}' not found.");
            }
        }

        var actor = new Actor
        {
            DisplayName = req.DisplayName.Trim(),
            Type = req.Type,
            Email = req.Email,
            LlmProviderConfigId = req.LlmProviderConfigId,
        };
        var membership = new Membership { Actor = actor, ProjectId = project.Id, Role = req.Role };
        db.Actors.Add(actor);
        db.Memberships.Add(membership);
        await db.SaveChangesAsync(ct);

        return membership.ToMemberDto();
    }

    /// <summary>Edit a project member: display name, email, role, and (for agents) the LLM binding.</summary>
    public async Task<MemberDto> UpdateMemberAsync(
        Guid projectId, Guid actorId, UpdateMemberRequest req, CancellationToken ct = default)
    {
        var membership = await db.Memberships
            .Include(m => m.Actor)
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.ActorId == actorId, ct)
            ?? throw new NotFoundException($"Member '{actorId}' not found in project '{projectId}'.");

        if (string.IsNullOrWhiteSpace(req.DisplayName))
        {
            throw new BadRequestException("Member display name is required.");
        }

        if (req.LlmProviderConfigId is not null)
        {
            if (membership.Actor.Type != ActorType.Agent)
            {
                throw new BadRequestException("Only Agent actors can be bound to an LLM provider config.");
            }
            if (!await db.LlmProviderConfigs.AnyAsync(c => c.Id == req.LlmProviderConfigId, ct))
            {
                throw new NotFoundException($"LLM provider config '{req.LlmProviderConfigId}' not found.");
            }
        }

        membership.Actor.DisplayName = req.DisplayName.Trim();
        membership.Actor.Email = req.Email;
        membership.Actor.LlmProviderConfigId =
            membership.Actor.Type == ActorType.Agent ? req.LlmProviderConfigId : null;
        membership.Role = req.Role;
        await db.SaveChangesAsync(ct);
        return membership.ToMemberDto();
    }

    /// <summary>Remove a member from a project. Deletes the membership only; the actor row is kept so
    /// their authored changes, comments, and audit entries remain valid.</summary>
    public async Task RemoveMemberAsync(Guid projectId, Guid actorId, CancellationToken ct = default)
    {
        var membership = await db.Memberships
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.ActorId == actorId, ct)
            ?? throw new NotFoundException($"Member '{actorId}' not found in project '{projectId}'.");
        db.Memberships.Remove(membership);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>App-level list of actors with their provider binding, project membership count, and
    /// whether they can be safely hard-deleted (no memberships and no references anywhere).</summary>
    public async Task<IReadOnlyList<ActorDto>> ListActorsAsync(
        bool agentsOnly, bool includeArchived = false, CancellationToken ct = default)
    {
        var query = db.Actors.AsQueryable();
        if (agentsOnly)
        {
            query = query.Where(a => a.Type == ActorType.Agent);
        }
        if (!includeArchived)
        {
            query = query.Where(a => !a.IsArchived);
        }

        return await query
            .OrderBy(a => a.DisplayName)
            .Select(a => new ActorDto(
                a.Id, a.DisplayName, a.Type, a.Email,
                a.LlmProviderConfigId,
                a.LlmProviderConfig != null ? a.LlmProviderConfig.Name : null,
                db.Memberships.Count(m => m.ActorId == a.Id),
                db.Memberships.All(m => m.ActorId != a.Id)
                    && db.ChangeRequests.All(c => c.CreatedByActorId != a.Id && c.AssigneeActorId != a.Id)
                    && db.Comments.All(c => c.AuthorActorId != a.Id)
                    && db.AuditEvents.All(e => e.ActorId != a.Id),
                a.IsArchived))
            .ToListAsync(ct);
    }

    /// <summary>Archive an actor: keep it (preserving history) but hide it from active lists. Unlike
    /// deletion this is never blocked — archiving is exactly what referenced actors use instead.</summary>
    public async Task ArchiveActorAsync(Guid id, bool archived, CancellationToken ct = default)
    {
        var actor = await db.Actors.FindAsync([id], ct)
            ?? throw new NotFoundException($"Actor '{id}' not found.");

        if (archived)
        {
            await EnsureNotLastSiteAdminAsync(actor, ct);
        }

        actor.IsArchived = archived;
        actor.ArchivedAt = archived ? DateTimeOffset.UtcNow : null;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>An "effective" site admin can actually sign in (IsSiteAdmin and not archived). Guards
    /// against removing/disabling the final one, which would lock everyone out of administration.
    /// No-op for actors that aren't currently effective site admins.</summary>
    public async Task EnsureNotLastSiteAdminAsync(Actor actor, CancellationToken ct = default)
    {
        if (!actor.IsSiteAdmin || actor.IsArchived)
        {
            return;
        }

        var anotherExists = await db.Actors
            .AnyAsync(a => a.Id != actor.Id && a.IsSiteAdmin && !a.IsArchived, ct);
        if (!anotherExists)
        {
            throw new BadRequestException("Can't remove the last site administrator.");
        }
    }

    /// <summary>Edit an actor's identity fields (display name, email). Like the LLM-provider edit, this
    /// is an app-level correction; project role and provider binding are managed via membership.</summary>
    public async Task<ActorDto> UpdateActorAsync(Guid id, UpdateActorRequest req, CancellationToken ct = default)
    {
        var actor = await db.Actors
            .Include(a => a.LlmProviderConfig)
            .FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException($"Actor '{id}' not found.");

        if (string.IsNullOrWhiteSpace(req.DisplayName))
        {
            throw new BadRequestException("Display name is required.");
        }

        // Email is the login identity, so it must be unique (case-insensitive). Normalize to match
        // CreateLocalUserAsync and the login/JIT lookups.
        var email = string.IsNullOrWhiteSpace(req.Email) ? null : req.Email.Trim().ToLowerInvariant();
        if (email is not null
            && await db.Actors.AnyAsync(a => a.Id != id && a.Email != null && a.Email.ToLower() == email, ct))
        {
            throw new BadRequestException("An actor with that email already exists.");
        }

        actor.DisplayName = req.DisplayName.Trim();
        actor.Email = email;
        await db.SaveChangesAsync(ct);

        return new ActorDto(
            actor.Id, actor.DisplayName, actor.Type, actor.Email,
            actor.LlmProviderConfigId, actor.LlmProviderConfig?.Name,
            await db.Memberships.CountAsync(m => m.ActorId == actor.Id, ct),
            !await db.Memberships.AnyAsync(m => m.ActorId == actor.Id, ct)
                && !await db.ChangeRequests.AnyAsync(c => c.CreatedByActorId == actor.Id || c.AssigneeActorId == actor.Id, ct)
                && !await db.Comments.AnyAsync(c => c.AuthorActorId == actor.Id, ct)
                && !await db.AuditEvents.AnyAsync(e => e.ActorId == actor.Id, ct),
            actor.IsArchived);
    }

    /// <summary>Hard-delete an actor. Blocked while it belongs to a project or is referenced by any
    /// change, comment, or audit event (those are kept to preserve history).</summary>
    public async Task DeleteActorAsync(Guid id, CancellationToken ct = default)
    {
        var actor = await db.Actors.FindAsync([id], ct)
            ?? throw new NotFoundException($"Actor '{id}' not found.");

        if (await db.Memberships.AnyAsync(m => m.ActorId == id, ct))
        {
            throw new BadRequestException("Actor is still a member of a project. Remove the membership first.");
        }
        var referenced =
            await db.ChangeRequests.AnyAsync(c => c.CreatedByActorId == id || c.AssigneeActorId == id, ct)
            || await db.Comments.AnyAsync(c => c.AuthorActorId == id, ct)
            || await db.AuditEvents.AnyAsync(e => e.ActorId == id, ct);
        if (referenced)
        {
            throw new BadRequestException("Actor is referenced by changes, comments, or audit history and can't be deleted.");
        }
        await EnsureNotLastSiteAdminAsync(actor, ct);

        // Remove the login credential first — its FK to Actor is Restrict, so it would otherwise block
        // the delete. Credentials aren't history, so dropping them with the actor is correct.
        var credential = await db.LocalCredentials.FirstOrDefaultAsync(c => c.ActorId == id, ct);
        if (credential is not null)
        {
            db.LocalCredentials.Remove(credential);
        }

        db.Actors.Remove(actor);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<MemberDto>> ListMembersAsync(Guid projectId, CancellationToken ct = default) =>
        await db.Memberships
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.Actor.DisplayName)
            // Explicit projection so EF JOINs to Actor (an extension-method projection would drop it).
            .Select(m => new MemberDto(
                m.ActorId, m.ProjectId, m.Actor.DisplayName, m.Actor.Type, m.Actor.Email, m.Role, m.Actor.LlmProviderConfigId))
            .ToListAsync(ct);

    /// <summary>Providers available to a project: its own plus website-wide (global) ones. Globals
    /// (ProjectId == null) are sorted first.</summary>
    public async Task<IReadOnlyList<LlmProviderConfigDto>> ListLlmProvidersAsync(Guid projectId, CancellationToken ct = default) =>
        await db.LlmProviderConfigs
            .Where(c => c.ProjectId == projectId || c.ProjectId == null)
            .OrderBy(c => c.ProjectId == null ? 0 : 1)
            .ThenBy(c => c.Name)
            .Select(c => c.ToDto())
            .ToListAsync(ct);

    /// <summary>Website-wide providers only (ProjectId == null).</summary>
    public async Task<IReadOnlyList<LlmProviderConfigDto>> ListGlobalLlmProvidersAsync(CancellationToken ct = default) =>
        await db.LlmProviderConfigs
            .Where(c => c.ProjectId == null)
            .OrderBy(c => c.Name)
            .Select(c => c.ToDto())
            .ToListAsync(ct);

    /// <summary>Create an LLM provider config. <paramref name="projectId"/> null = website-wide
    /// (available to every project); otherwise scoped to that project.</summary>
    public async Task<LlmProviderConfigDto> CreateLlmProviderAsync(
        Guid? projectId, CreateLlmProviderRequest req, CancellationToken ct = default)
    {
        if (projectId is not null && !await db.Projects.AnyAsync(p => p.Id == projectId, ct))
        {
            throw new NotFoundException($"Project '{projectId}' not found.");
        }

        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Model))
        {
            throw new BadRequestException("Provider name and model are required.");
        }

        var config = new LlmProviderConfig
        {
            ProjectId = projectId,
            Type = req.Type,
            Name = req.Name,
            BaseUrl = req.BaseUrl,
            Model = req.Model,
            ApiKeySecretRef = req.ApiKeySecretRef,
        };
        db.LlmProviderConfigs.Add(config);
        await db.SaveChangesAsync(ct);
        return config.ToDto();
    }

    /// <summary>Edit an existing provider config (project-scoped or global) — fix the model, base URL,
    /// key reference, etc., or enable/disable it.</summary>
    public async Task<LlmProviderConfigDto> UpdateLlmProviderAsync(
        Guid id, UpdateLlmProviderRequest req, CancellationToken ct = default)
    {
        var config = await db.LlmProviderConfigs.FindAsync([id], ct)
            ?? throw new NotFoundException($"LLM provider config '{id}' not found.");

        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Model))
        {
            throw new BadRequestException("Provider name and model are required.");
        }

        config.Type = req.Type;
        config.Name = req.Name;
        config.BaseUrl = req.BaseUrl;
        config.Model = req.Model;
        config.ApiKeySecretRef = req.ApiKeySecretRef;
        config.Enabled = req.Enabled;
        await db.SaveChangesAsync(ct);
        return config.ToDto();
    }

    /// <summary>Delete a provider config. Blocked while any agent still references it (reassign first).</summary>
    public async Task DeleteLlmProviderAsync(Guid id, CancellationToken ct = default)
    {
        var config = await db.LlmProviderConfigs.FindAsync([id], ct)
            ?? throw new NotFoundException($"LLM provider config '{id}' not found.");

        var inUse = await db.Actors.CountAsync(a => a.LlmProviderConfigId == id, ct);
        if (inUse > 0)
        {
            var actors = await db.Actors.Where(a => a.LlmProviderConfigId == id).ToListAsync(ct);
            throw new BadRequestException($"Provider is in use by {inUse} agent(s). Reassign them before deleting.");
        }

        db.LlmProviderConfigs.Remove(config);
        await db.SaveChangesAsync(ct);
    }
}
