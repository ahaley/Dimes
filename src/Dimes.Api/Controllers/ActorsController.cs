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
/// (account lockout). The List read stays open so the board can resolve assignee and comment-author
/// names — but it discloses only identity (email is stripped for non-admins). The detail read, which
/// exposes a user's email and full cross-project role map, is site-admin only.</summary>
[ApiController]
[Route("api/actors")]
public class ActorsController(ProjectService projects, ICurrentActor currentActor) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ActorDto>>> List(
        [FromQuery] bool agentsOnly = true, [FromQuery] bool includeArchived = false, CancellationToken ct = default)
        => Ok(await projects.ListActorsAsync(agentsOnly, currentActor.IsSiteAdmin, includeArchived, ct));

    /// <summary>Actor detail: identity + provider + the actor's per-project role map. Site-admin only —
    /// it crosses the project boundary the rest of the API guards and exposes the login email.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(DimesClaims.SiteAdminPolicy)]
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
