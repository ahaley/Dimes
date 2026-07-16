using Dimes.Api.Auth;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Dimes.Api.Controllers;

[ApiController]
public class WorkOrdersController(
    WorkOrderService workOrders, ProjectService projects, ICurrentActor currentActor) : ControllerBase
{
    /// <summary>Report-back entry point for an agent executing an exported work order. Anonymous: the agent
    /// has no Dimes session, and the unguessable per-export token embedded in the work-order file acts as the
    /// capability — the same posture as the observation capture endpoint. The token is deliberately narrow:
    /// it reaches only its own work order's items, carries only the exporting actor's authority (re-checked
    /// on every call), and can never change a change's status.</summary>
    [AllowAnonymous]
    [EnableCors(CorsPolicies.Ingest)]
    [EnableRateLimiting(RateLimitPolicies.Ingest)]
    [RequestSizeLimit(256 * 1024)]
    [HttpPost("api/work-orders/{token}/results")]
    public async Task<ActionResult<WorkOrderResultsDto>> Report(
        string token, WorkOrderResultsRequest req, CancellationToken ct)
        => Ok(await workOrders.ReportResultsAsync(token, req, ct));

    /// <summary>The project's most recent export and how much of it has reported back.</summary>
    [HttpGet("api/projects/{projectId:guid}/work-orders/latest")]
    public async Task<ActionResult<WorkOrderSummaryDto?>> Latest(Guid projectId, CancellationToken ct)
    {
        await projects.EnsureProjectReadAsync(projectId, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await workOrders.LatestAsync(projectId, ct));
    }
}
