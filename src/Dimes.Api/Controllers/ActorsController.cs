using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Dimes.Api.Controllers;

/// <summary>App-level actor management — view actors (agents by default) and delete orphaned,
/// unreferenced ones. Project membership is managed under /api/projects/{id}/members.</summary>
[ApiController]
[Route("api/actors")]
public class ActorsController(ProjectService projects) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ActorDto>>> List(
        [FromQuery] bool agentsOnly = true, [FromQuery] bool includeArchived = false, CancellationToken ct = default)
        => Ok(await projects.ListActorsAsync(agentsOnly, includeArchived, ct));

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await projects.ArchiveActorAsync(id, archived: true, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/unarchive")]
    public async Task<IActionResult> Unarchive(Guid id, CancellationToken ct)
    {
        await projects.ArchiveActorAsync(id, archived: false, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await projects.DeleteActorAsync(id, ct);
        return NoContent();
    }
}
