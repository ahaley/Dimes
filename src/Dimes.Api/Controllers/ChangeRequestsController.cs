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

    [HttpGet("api/projects/{projectId:guid}/changes")]
    public async Task<ActionResult<IReadOnlyList<ChangeRequestDto>>> List(
        Guid projectId, [FromQuery] ChangeStatus? status, CancellationToken ct)
    {
        await projects.EnsureProjectReadAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await changes.ListAsync(projectId, status, ct));
    }

    [HttpGet("api/changes/{id:guid}")]
    public async Task<ActionResult<ChangeRequestDetailDto>> Get(Guid id, CancellationToken ct)
    {
        await changes.EnsureCanReadChangeAsync(id, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await changes.GetDetailAsync(id, ct));
    }

    [HttpPatch("api/changes/{id:guid}")]
    public async Task<ActionResult<ChangeRequestDto>> UpdateDetails(Guid id, UpdateChangeDetailsRequest req, CancellationToken ct)
        => Ok(await changes.UpdateDetailsAsync(id, currentActor.ActorId, req, ct));

    [HttpPost("api/changes/{id:guid}/transition")]
    public async Task<ActionResult<ChangeRequestDto>> Transition(Guid id, TransitionChangeRequest req, CancellationToken ct)
        => Ok(await changes.TransitionAsync(id, currentActor.ActorId, req, ct));

    [HttpPost("api/changes/{id:guid}/comments")]
    public async Task<ActionResult<CommentDto>> AddComment(Guid id, AddCommentRequest req, CancellationToken ct)
        => Ok(await changes.AddCommentAsync(id, currentActor.ActorId, req, ct));

    [HttpPost("api/changes/{id:guid}/scm-links")]
    public async Task<ActionResult<ScmLinkDto>> AddScmLink(Guid id, AddScmLinkRequest req, CancellationToken ct)
        => Ok(await scm.AddLinkAsync(id, currentActor.ActorId, req, ct));

    /// <summary>Recommend-only agent commentary — posts a comment, never changes state.</summary>
    [HttpPost("api/changes/{id:guid}/agent-comment")]
    public async Task<ActionResult<CommentDto>> AgentComment(Guid id, AgentCommentRequest req, CancellationToken ct)
        => Ok(await commentary.CommentOnChangeAsync(id, req.AgentActorId, ct));

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
}
