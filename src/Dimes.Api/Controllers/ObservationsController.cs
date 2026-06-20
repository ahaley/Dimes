using Dimes.Api.Auth;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Dimes.Api.Controllers;

[ApiController]
public class ObservationsController(
    ObservationService observations, ProjectService projects, ICurrentActor currentActor) : ControllerBase
{
    /// <summary>Capture entry point for sources (SDK, Seq). Aggregates by fingerprint. Anonymous: the
    /// host app has no Dimes session, and the unguessable source id acts as the capability. (A future
    /// slice may add per-source ingest tokens.)</summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Ingest)]
    [RequestSizeLimit(256 * 1024)]
    [HttpPost("api/sources/{sourceId:guid}/observations")]
    public async Task<ActionResult<ObservationDto>> Ingest(
        Guid sourceId, IngestObservationRequest req, CancellationToken ct)
        => Ok(await observations.IngestAsync(sourceId, req, ct));

    /// <summary>The observation inbox for a project, optionally filtered by status.</summary>
    [HttpGet("api/projects/{projectId:guid}/observations")]
    public async Task<ActionResult<IReadOnlyList<ObservationDto>>> Inbox(
        Guid projectId, [FromQuery] ObservationStatus? status, CancellationToken ct)
    {
        await projects.EnsureProjectReadAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await observations.ListInboxAsync(projectId, status, ct));
    }

    [HttpPost("api/observations/{id:guid}/cluster")]
    public async Task<ActionResult<ObservationDto>> Cluster(Guid id, CancellationToken ct)
        => Ok(await observations.ClusterAsync(id, currentActor.ActorId, ct));

    [HttpPost("api/observations/{id:guid}/dismiss")]
    public async Task<ActionResult<ObservationDto>> Dismiss(Guid id, DismissObservationRequest req, CancellationToken ct)
        => Ok(await observations.DismissAsync(id, currentActor.ActorId, req.Reason, ct));

    [HttpPost("api/observations/{id:guid}/promote")]
    public async Task<ActionResult<ChangeRequestDto>> Promote(Guid id, PromoteObservationRequest req, CancellationToken ct)
        => Ok(await observations.PromoteAsync(id, currentActor.ActorId, req, ct));
}
