using Dimes.Api.Auth;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Dimes.Api.Controllers;

/// <summary>Capture Assist Mode: a conversational helper that drives a project Agent's LLM to help a
/// user shape a loose idea into a change request. Recommend-only and stateless — it returns the
/// assistant's reply; the client holds the conversation and creates the change via the normal
/// create endpoint when the user confirms.</summary>
[ApiController]
public class CaptureAssistController(
    CaptureAssistService assist,
    ProjectService projects,
    ICurrentActor currentActor) : ControllerBase
{
    [HttpPost("api/projects/{projectId:guid}/capture-assist/chat")]
    public async Task<ActionResult<CaptureAssistReplyDto>> Chat(
        Guid projectId, CaptureAssistChatRequest req, CancellationToken ct)
    {
        await projects.EnsureProjectReadAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await assist.ChatAsync(projectId, req, ct));
    }

    /// <summary>Freestyle Mode: turn a freeform markdown brief into a list of editable change-order
    /// proposals. Recommend-only — nothing is created until the user confirms a batch create.</summary>
    [HttpPost("api/projects/{projectId:guid}/capture-assist/proposals")]
    public async Task<ActionResult<GenerateProposalsReplyDto>> Proposals(
        Guid projectId, GenerateProposalsRequest req, CancellationToken ct)
    {
        await projects.EnsureProjectReadAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await assist.GenerateProposalsAsync(projectId, req, ct));
    }
}
