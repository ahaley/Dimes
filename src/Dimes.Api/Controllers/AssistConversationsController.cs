using Dimes.Api.Auth;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Dimes.Api.Controllers;

/// <summary>Persisted, two-way Capture Assist with a human assistant. The requester starts a
/// conversation (bubbled into the assistant's inbox), both sides exchange messages, and it closes when
/// the change is captured. Reads are gated on project membership; posting is gated to participants.</summary>
[ApiController]
public class AssistConversationsController(
    AssistConversationService assist, ProjectService projects, ICurrentActor currentActor) : ControllerBase
{
    [HttpPost("api/projects/{projectId:guid}/assist/conversations")]
    public async Task<ActionResult<AssistConversationDto>> Start(
        Guid projectId, StartAssistConversationRequest req, CancellationToken ct)
    {
        await projects.EnsureProjectReadAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await assist.StartAsync(projectId, currentActor.ActorId, req, ct));
    }

    [HttpGet("api/projects/{projectId:guid}/assist/conversations")]
    public async Task<ActionResult<IReadOnlyList<AssistConversationSummaryDto>>> List(
        Guid projectId, [FromQuery] string? role, [FromQuery] AssistConversationStatus? status, CancellationToken ct)
    {
        await projects.EnsureProjectReadAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await assist.ListAsync(projectId, currentActor.ActorId, role ?? "assistant", status, ct));
    }

    [HttpGet("api/projects/{projectId:guid}/assist/conversations/{conversationId:guid}")]
    public async Task<ActionResult<AssistConversationDto>> Get(
        Guid projectId, Guid conversationId, CancellationToken ct)
    {
        await projects.EnsureProjectReadAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await assist.GetAsync(projectId, conversationId, currentActor.ActorId, currentActor.IsSiteAdmin, ct));
    }

    [HttpPost("api/projects/{projectId:guid}/assist/conversations/{conversationId:guid}/messages")]
    public async Task<ActionResult<AssistConversationDto>> Post(
        Guid projectId, Guid conversationId, PostAssistMessageRequest req, CancellationToken ct)
    {
        await projects.EnsureProjectReadAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await assist.PostMessageAsync(projectId, conversationId, currentActor.ActorId, req, ct));
    }

    [HttpPost("api/projects/{projectId:guid}/assist/conversations/{conversationId:guid}/close")]
    public async Task<ActionResult<AssistConversationDto>> Close(
        Guid projectId, Guid conversationId, CloseAssistConversationRequest req, CancellationToken ct)
    {
        await projects.EnsureProjectReadAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await assist.CloseAsync(projectId, conversationId, currentActor.ActorId, req.ChangeRequestId, ct));
    }
}
