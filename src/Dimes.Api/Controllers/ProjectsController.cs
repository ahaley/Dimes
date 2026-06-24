using Dimes.Api.Auth;
using Dimes.Api.Contracts;
using Dimes.Api.Realtime;
using Dimes.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dimes.Api.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController(
    ProjectService projects, ObservationService observations, ICurrentActor currentActor, IBoardNotifier notifier) : ControllerBase
{
    /// <summary>Create a project. Restricted to site administrators — creation is an instance-level
    /// action (there's no project yet to scope a per-project role to).</summary>
    [HttpPost]
    [Authorize(DimesClaims.SiteAdminPolicy)]
    public async Task<ActionResult<ProjectDto>> Create(CreateProjectRequest req, CancellationToken ct)
    {
        var project = await projects.CreateAsync(req, ct);
        await notifier.ProjectsChangedAsync(ct); // refresh every client's sidebar list
        return CreatedAtAction(nameof(List), new { }, project);
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProjectDto>>> List([FromQuery] bool includeArchived, CancellationToken ct)
        => Ok(await projects.ListAsync(currentActor.ActorId, currentActor.IsSiteAdmin, includeArchived, ct));

    /// <summary>Save the caller's personal project ordering (sidebar order; the top one is their default).</summary>
    [HttpPost("reorder")]
    public async Task<IActionResult> Reorder(ReorderProjectsRequest req, CancellationToken ct)
    {
        await projects.ReorderProjectsAsync(currentActor.ActorId, req, ct);
        return NoContent();
    }

    /// <summary>Edit a project's name and description. Authority: a project Maintainer or a site admin.</summary>
    [HttpPatch("{projectId:guid}")]
    public async Task<ActionResult<ProjectDto>> Update(Guid projectId, UpdateProjectRequest req, CancellationToken ct)
    {
        var project = await projects.UpdateAsync(projectId, req, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        await notifier.ProjectsChangedAsync(ct); // refresh every client's sidebar list
        return Ok(project);
    }

    /// <summary>Archive a project (soft-delete). Authority: a project Maintainer or a site admin.</summary>
    [HttpPost("{projectId:guid}/archive")]
    public async Task<IActionResult> Archive(Guid projectId, CancellationToken ct)
    {
        await projects.ArchiveProjectAsync(projectId, archived: true, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        await notifier.ProjectsChangedAsync(ct);
        return NoContent();
    }

    [HttpPost("{projectId:guid}/unarchive")]
    public async Task<IActionResult> Unarchive(Guid projectId, CancellationToken ct)
    {
        await projects.ArchiveProjectAsync(projectId, archived: false, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        await notifier.ProjectsChangedAsync(ct);
        return NoContent();
    }

    [HttpGet("{projectId:guid}/members")]
    public async Task<ActionResult<IReadOnlyList<MemberDto>>> ListMembers(Guid projectId, CancellationToken ct)
    {
        await projects.EnsureProjectReadAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await projects.ListMembersAsync(projectId, ct));
    }

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
    {
        await projects.EnsureProjectReadAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await projects.ListLlmProvidersAsync(projectId, ct));
    }

    [HttpGet("{projectId:guid}/sources")]
    public async Task<ActionResult<IReadOnlyList<ObservationSourceDto>>> ListSources(Guid projectId, CancellationToken ct)
    {
        await projects.EnsureProjectReadAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await observations.ListSourcesAsync(projectId, ct));
    }

    [HttpPost("{projectId:guid}/sources")]
    public async Task<ActionResult<ObservationSourceDto>> CreateSource(
        Guid projectId, CreateSourceRequest req, CancellationToken ct)
    {
        // A source id is an anonymous ingest capability (the [AllowAnonymous] capture endpoint trusts
        // it), so minting one is project configuration — gate it like the sibling provider create.
        await projects.EnsureProjectAdminAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await observations.CreateSourceAsync(projectId, req, ct));
    }

    [HttpPost("{projectId:guid}/llm-providers")]
    public async Task<ActionResult<LlmProviderConfigDto>> CreateLlmProvider(
        Guid projectId, CreateLlmProviderRequest req, CancellationToken ct)
    {
        await projects.EnsureProjectAdminAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await projects.CreateLlmProviderAsync(projectId, req, ct));
    }
}
