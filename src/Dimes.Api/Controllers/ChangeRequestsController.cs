using System.Text;
using Dimes.Api.Auth;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Dimes.Api.Controllers;

[ApiController]
public class ChangeRequestsController(
    ChangeRequestService changes,
    CommentaryService commentary,
    ScmService scm,
    ProjectService projects,
    ICurrentActor currentActor) : ControllerBase
{
    [HttpPost("api/projects/{projectId:guid}/changes")]
    public async Task<ActionResult<ChangeRequestDto>> Create(
        Guid projectId, CreateChangeRequest req, CancellationToken ct)
    {
        var change = await changes.CreateAsync(projectId, currentActor.ActorId, req, ct);
        return CreatedAtAction(nameof(Get), new { id = change.Id }, change);
    }

    /// <summary>Create a batch of changes in one transaction (Capture Assist Freestyle Mode confirm).</summary>
    [HttpPost("api/projects/{projectId:guid}/changes/batch")]
    public async Task<ActionResult<IReadOnlyList<ChangeRequestDto>>> CreateBatch(
        Guid projectId, CreateChangesRequest req, CancellationToken ct)
        => Ok(await changes.CreateManyAsync(projectId, currentActor.ActorId, req.Changes, ct));

    /// <summary>Persist a manual within-column order from board drag-and-drop.</summary>
    [HttpPost("api/projects/{projectId:guid}/changes/reorder")]
    public async Task<IActionResult> Reorder(Guid projectId, ReorderChangesRequest req, CancellationToken ct)
    {
        await changes.ReorderAsync(projectId, currentActor.ActorId, req, ct);
        return NoContent();
    }

    [HttpGet("api/projects/{projectId:guid}/changes")]
    public async Task<ActionResult<IReadOnlyList<ChangeRequestDto>>> List(
        Guid projectId, [FromQuery] ChangeStatus? status, CancellationToken ct)
    {
        await projects.EnsureProjectReadAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await changes.ListAsync(projectId, status, ct));
    }

    /// <summary>Per-project counts of open change requests assigned to the current actor — feeds the
    /// sidebar "assigned to you" indicator. Scoped to the caller's own assignments.</summary>
    [HttpGet("api/me/assignment-counts")]
    public async Task<ActionResult<IReadOnlyList<ProjectAssignmentCountDto>>> AssignmentCounts(CancellationToken ct)
        => Ok(await changes.AssignedOpenCountsAsync(currentActor.ActorId, ct));

    [HttpGet("api/changes/{id:guid}")]
    public async Task<ActionResult<ChangeRequestDetailDto>> Get(Guid id, CancellationToken ct)
    {
        await changes.EnsureCanReadChangeAsync(id, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await changes.GetDetailAsync(id, ct));
    }

    [HttpPatch("api/changes/{id:guid}")]
    public async Task<ActionResult<ChangeRequestDto>> UpdateDetails(Guid id, UpdateChangeDetailsRequest req, CancellationToken ct)
        => Ok(await changes.UpdateDetailsAsync(id, currentActor.ActorId, req, ct));

    /// <summary>Set or clear a change's recipient (Contributor+; recipient must be a project member).</summary>
    [HttpPatch("api/changes/{id:guid}/assignee")]
    public async Task<ActionResult<ChangeRequestDto>> Assign(Guid id, AssignChangeRequest req, CancellationToken ct)
        => Ok(await changes.AssignAsync(id, currentActor.ActorId, req, ct));

    [HttpPost("api/changes/{id:guid}/transition")]
    public async Task<ActionResult<ChangeRequestDto>> Transition(Guid id, TransitionChangeRequest req, CancellationToken ct)
        => Ok(await changes.TransitionAsync(id, currentActor.ActorId, req, ct));

    /// <summary>Compose an existing change request into this Epic (Contributor+).</summary>
    [HttpPost("api/changes/{epicId:guid}/children")]
    public async Task<ActionResult<ChangeRequestDto>> AddChild(Guid epicId, AddEpicChildRequest req, CancellationToken ct)
        => Ok(await changes.AddChildAsync(epicId, currentActor.ActorId, req.ChildId, ct));

    /// <summary>Break a composed change out of this Epic (Contributor+).</summary>
    [HttpDelete("api/changes/{epicId:guid}/children/{childId:guid}")]
    public async Task<ActionResult<ChangeRequestDto>> RemoveChild(Guid epicId, Guid childId, CancellationToken ct)
        => Ok(await changes.RemoveChildAsync(epicId, currentActor.ActorId, childId, ct));

    /// <summary>Move this Epic and all its composed children toward a target status (best-effort; skips
    /// members for which the move is illegal or unauthorized). Returns which moved and which were skipped.</summary>
    [HttpPost("api/changes/{epicId:guid}/bulk-transition")]
    public async Task<ActionResult<BulkTransitionResultDto>> BulkTransition(
        Guid epicId, BulkTransitionRequest req, CancellationToken ct)
        => Ok(await changes.BulkTransitionAsync(epicId, currentActor.ActorId, req.Target, req.Reason, ct));

    [HttpPost("api/changes/{id:guid}/comments")]
    public async Task<ActionResult<CommentDto>> AddComment(Guid id, AddCommentRequest req, CancellationToken ct)
        => Ok(await changes.AddCommentAsync(id, currentActor.ActorId, req, ct));

    [HttpPost("api/changes/{id:guid}/scm-links")]
    public async Task<ActionResult<ScmLinkDto>> AddScmLink(Guid id, AddScmLinkRequest req, CancellationToken ct)
        => Ok(await scm.AddLinkAsync(id, currentActor.ActorId, req, ct));

    /// <summary>Recommend-only agent commentary — posts a comment, never changes state. The caller must
    /// be a member of the change's project (CommentaryService enforces it), like adding a human comment.</summary>
    [HttpPost("api/changes/{id:guid}/agent-comment")]
    public async Task<ActionResult<CommentDto>> AgentComment(Guid id, AgentCommentRequest req, CancellationToken ct)
        => Ok(await commentary.CommentOnChangeAsync(
            id, req.AgentActorId, currentActor.ActorId, currentActor.IsSiteAdmin, ct));

    [HttpGet("api/changes/{id:guid}/audit")]
    public async Task<ActionResult<IReadOnlyList<AuditEventDto>>> Audit(Guid id, CancellationToken ct)
    {
        await changes.EnsureCanReadChangeAsync(id, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await changes.GetAuditAsync(id, ct));
    }

    /// <summary>Download a Claude Code work-order markdown of the project's In-Development changes.</summary>
    [HttpGet("api/projects/{projectId:guid}/export/in-development")]
    public async Task<IActionResult> ExportInDevelopment(Guid projectId, CancellationToken ct)
    {
        await projects.EnsureProjectReadAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        var export = await changes.ExportInDevelopmentAsync(projectId, ct);
        return File(Encoding.UTF8.GetBytes(export.Markdown), "text/markdown", export.FileName);
    }

    /// <summary>Read the project's editable export work-order guidance (or the built-in default).</summary>
    [HttpGet("api/projects/{projectId:guid}/export/instruction")]
    public async Task<ActionResult<ExportInstructionDto>> GetExportInstruction(Guid projectId, CancellationToken ct)
    {
        await projects.EnsureProjectReadAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await projects.GetExportInstructionAsync(projectId, ct));
    }

    /// <summary>Edit or reset the project's export work-order guidance (Maintainer or site admin).</summary>
    [HttpPut("api/projects/{projectId:guid}/export/instruction")]
    public async Task<ActionResult<ExportInstructionDto>> UpdateExportInstruction(
        Guid projectId, UpdateExportInstructionRequest req, CancellationToken ct)
        => Ok(await projects.UpdateExportInstructionAsync(projectId, req, currentActor.ActorId, currentActor.IsSiteAdmin, ct));
}
