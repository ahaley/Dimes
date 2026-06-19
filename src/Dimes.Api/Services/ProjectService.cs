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

    public async Task<IReadOnlyList<MemberDto>> ListMembersAsync(Guid projectId, CancellationToken ct = default) =>
        await db.Memberships
            .Where(m => m.ProjectId == projectId)
            .OrderBy(m => m.Actor.DisplayName)
            // Explicit projection so EF JOINs to Actor (an extension-method projection would drop it).
            .Select(m => new MemberDto(
                m.ActorId, m.ProjectId, m.Actor.DisplayName, m.Actor.Type, m.Actor.Email, m.Role, m.Actor.LlmProviderConfigId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LlmProviderConfigDto>> ListLlmProvidersAsync(Guid projectId, CancellationToken ct = default) =>
        await db.LlmProviderConfigs
            .Where(c => c.ProjectId == projectId)
            .OrderBy(c => c.Name)
            .Select(c => c.ToDto())
            .ToListAsync(ct);

    public async Task<LlmProviderConfigDto> CreateLlmProviderAsync(
        Guid projectId, CreateLlmProviderRequest req, CancellationToken ct = default)
    {
        var project = await db.Projects.FindAsync([projectId], ct)
            ?? throw new NotFoundException($"Project '{projectId}' not found.");

        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Model))
        {
            throw new BadRequestException("Provider name and model are required.");
        }

        var config = new LlmProviderConfig
        {
            ProjectId = project.Id,
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
}
