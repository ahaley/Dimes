using Dimes.Api.Auth;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dimes.Api.Controllers;

/// <summary>App-level actor management — view actors (agents by default) and delete orphaned,
/// unreferenced ones. Project membership is managed under /api/projects/{id}/members.
///
/// The mutations here are site-admin only: an actor's email is its login identity (editing another
/// user's email breaks their sign-in / enables impersonation) and archiving rejects future logins
/// (account lockout). Reads stay open to any authenticated user — the board resolves assignee and
/// comment-author names from this list.</summary>
[ApiController]
[Route("api/actors")]
public class ActorsController(ProjectService projects) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ActorDto>>> List(
        [FromQuery] bool agentsOnly = true, [FromQuery] bool includeArchived = false, CancellationToken ct = default)
        => Ok(await projects.ListActorsAsync(agentsOnly, includeArchived, ct));

    /// <summary>Actor detail (identity + provider + per-project roles). Read-only, like List — open to
    /// any authenticated user so the actor presentation can be viewed.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ActorDetailDto>> Get(Guid id, CancellationToken ct)
        => Ok(await projects.GetActorAsync(id, ct));

    [HttpPatch("{id:guid}")]
    [Authorize(DimesClaims.SiteAdminPolicy)]
    public async Task<ActionResult<ActorDto>> Update(Guid id, UpdateActorRequest req, CancellationToken ct)
        => Ok(await projects.UpdateActorAsync(id, req, ct));

    [HttpPost("{id:guid}/archive")]
    [Authorize(DimesClaims.SiteAdminPolicy)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await projects.ArchiveActorAsync(id, archived: true, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/unarchive")]
    [Authorize(DimesClaims.SiteAdminPolicy)]
    public async Task<IActionResult> Unarchive(Guid id, CancellationToken ct)
    {
        await projects.ArchiveActorAsync(id, archived: false, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(DimesClaims.SiteAdminPolicy)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await projects.DeleteActorAsync(id, ct);
        return NoContent();
    }
}
