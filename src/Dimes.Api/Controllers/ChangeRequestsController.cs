using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Dimes.Api.Controllers;

[ApiController]
public class ChangeRequestsController(
    ChangeRequestService changes,
    CommentaryService commentary,
    ScmService scm) : ControllerBase
{
    [HttpPost("api/projects/{projectId:guid}/changes")]
    public async Task<ActionResult<ChangeRequestDto>> Create(
        Guid projectId, CreateChangeRequest req, CancellationToken ct)
    {
        var change = await changes.CreateAsync(projectId, req, ct);
        return CreatedAtAction(nameof(Get), new { id = change.Id }, change);
    }

    [HttpGet("api/projects/{projectId:guid}/changes")]
    public async Task<ActionResult<IReadOnlyList<ChangeRequestDto>>> List(
        Guid projectId, [FromQuery] ChangeStatus? status, CancellationToken ct)
        => Ok(await changes.ListAsync(projectId, status, ct));

    [HttpGet("api/changes/{id:guid}")]
    public async Task<ActionResult<ChangeRequestDetailDto>> Get(Guid id, CancellationToken ct)
        => Ok(await changes.GetDetailAsync(id, ct));

    [HttpPost("api/changes/{id:guid}/transition")]
    public async Task<ActionResult<ChangeRequestDto>> Transition(Guid id, TransitionChangeRequest req, CancellationToken ct)
        => Ok(await changes.TransitionAsync(id, req, ct));

    [HttpPost("api/changes/{id:guid}/comments")]
    public async Task<ActionResult<CommentDto>> AddComment(Guid id, AddCommentRequest req, CancellationToken ct)
        => Ok(await changes.AddCommentAsync(id, req, ct));

    [HttpPost("api/changes/{id:guid}/scm-links")]
    public async Task<ActionResult<ScmLinkDto>> AddScmLink(Guid id, AddScmLinkRequest req, CancellationToken ct)
        => Ok(await scm.AddLinkAsync(id, req, ct));

    /// <summary>Recommend-only agent commentary — posts a comment, never changes state.</summary>
    [HttpPost("api/changes/{id:guid}/agent-comment")]
    public async Task<ActionResult<CommentDto>> AgentComment(Guid id, AgentCommentRequest req, CancellationToken ct)
        => Ok(await commentary.CommentOnChangeAsync(id, req.AgentActorId, ct));

    [HttpGet("api/changes/{id:guid}/audit")]
    public async Task<ActionResult<IReadOnlyList<AuditEventDto>>> Audit(Guid id, CancellationToken ct)
        => Ok(await changes.GetAuditAsync(id, ct));
}
