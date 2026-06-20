using Dimes.Api.Auth;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Dimes.Api.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController(ProjectService projects, ObservationService observations, ICurrentActor currentActor) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<ProjectDto>> Create(CreateProjectRequest req, CancellationToken ct)
    {
        var project = await projects.CreateAsync(req, ct);
        return CreatedAtAction(nameof(List), new { }, project);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProjectDto>>> List(CancellationToken ct)
        => Ok(await projects.ListAsync(currentActor.ActorId, currentActor.IsSiteAdmin, ct));

    [HttpGet("{projectId:guid}/members")]
    public async Task<ActionResult<IReadOnlyList<MemberDto>>> ListMembers(Guid projectId, CancellationToken ct)
        => Ok(await projects.ListMembersAsync(projectId, ct));

    [HttpPost("{projectId:guid}/members")]
    public async Task<ActionResult<MemberDto>> AddMember(Guid projectId, AddMemberRequest req, CancellationToken ct)
    {
        await projects.EnsureProjectAdminAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await projects.AddMemberAsync(projectId, req, ct));
    }

    [HttpPatch("{projectId:guid}/members/{actorId:guid}")]
    public async Task<ActionResult<MemberDto>> UpdateMember(Guid projectId, Guid actorId, UpdateMemberRequest req, CancellationToken ct)
    {
        await projects.EnsureProjectAdminAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await projects.UpdateMemberAsync(projectId, actorId, req, ct));
    }

    /// <summary>Link an existing actor (site user) to the project, or change their role — no new actor.</summary>
    [HttpPut("{projectId:guid}/members/{actorId:guid}")]
    public async Task<ActionResult<MemberDto>> AssignMember(Guid projectId, Guid actorId, SetMemberRoleRequest req, CancellationToken ct)
    {
        await projects.EnsureProjectAdminAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await projects.AssignMemberAsync(projectId, actorId, req.Role, ct));
    }

    [HttpDelete("{projectId:guid}/members/{actorId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid projectId, Guid actorId, CancellationToken ct)
    {
        await projects.EnsureProjectAdminAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        await projects.RemoveMemberAsync(projectId, actorId, ct);
        return NoContent();
    }

    [HttpGet("{projectId:guid}/llm-providers")]
    public async Task<ActionResult<IReadOnlyList<LlmProviderConfigDto>>> ListLlmProviders(Guid projectId, CancellationToken ct)
        => Ok(await projects.ListLlmProvidersAsync(projectId, ct));

    [HttpGet("{projectId:guid}/sources")]
    public async Task<ActionResult<IReadOnlyList<ObservationSourceDto>>> ListSources(Guid projectId, CancellationToken ct)
        => Ok(await observations.ListSourcesAsync(projectId, ct));

    [HttpPost("{projectId:guid}/sources")]
    public async Task<ActionResult<ObservationSourceDto>> CreateSource(
        Guid projectId, CreateSourceRequest req, CancellationToken ct)
        => Ok(await observations.CreateSourceAsync(projectId, req, ct));

    [HttpPost("{projectId:guid}/llm-providers")]
    public async Task<ActionResult<LlmProviderConfigDto>> CreateLlmProvider(
        Guid projectId, CreateLlmProviderRequest req, CancellationToken ct)
    {
        await projects.EnsureProjectAdminAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await projects.CreateLlmProviderAsync(projectId, req, ct));
    }
}
