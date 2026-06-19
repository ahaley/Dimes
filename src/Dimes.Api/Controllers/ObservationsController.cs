using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Dimes.Api.Controllers;

[ApiController]
public class ObservationsController(ObservationService observations) : ControllerBase
{
    /// <summary>Capture entry point for sources (SDK, Seq). Aggregates by fingerprint.</summary>
    [HttpPost("api/sources/{sourceId:guid}/observations")]
    public async Task<ActionResult<ObservationDto>> Ingest(
        Guid sourceId, IngestObservationRequest req, CancellationToken ct)
        => Ok(await observations.IngestAsync(sourceId, req, ct));

    /// <summary>The observation inbox for a project, optionally filtered by status.</summary>
    [HttpGet("api/projects/{projectId:guid}/observations")]
    public async Task<ActionResult<IReadOnlyList<ObservationDto>>> Inbox(
        Guid projectId, [FromQuery] ObservationStatus? status, CancellationToken ct)
        => Ok(await observations.ListInboxAsync(projectId, status, ct));

    [HttpPost("api/observations/{id:guid}/cluster")]
    public async Task<ActionResult<ObservationDto>> Cluster(Guid id, ActorActionRequest req, CancellationToken ct)
        => Ok(await observations.ClusterAsync(id, req.ActorId, ct));

    [HttpPost("api/observations/{id:guid}/dismiss")]
    public async Task<ActionResult<ObservationDto>> Dismiss(Guid id, DismissObservationRequest req, CancellationToken ct)
        => Ok(await observations.DismissAsync(id, req.ActorId, req.Reason, ct));

    [HttpPost("api/observations/{id:guid}/promote")]
    public async Task<ActionResult<ChangeRequestDto>> Promote(Guid id, PromoteObservationRequest req, CancellationToken ct)
        => Ok(await observations.PromoteAsync(id, req, ct));
}
