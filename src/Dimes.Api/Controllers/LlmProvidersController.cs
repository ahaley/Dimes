using Dimes.Api.Auth;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Dimes.Api.Controllers;

/// <summary>Website-wide (global) LLM providers — available to every project. Per-project providers
/// live under <c>/api/projects/{id}/llm-providers</c>. Mutating a global provider is site-admin
/// authority; the by-id update/delete also accept project-scoped configs and defer to that project's
/// Maintainer via <see cref="ProjectService.EnsureProviderAdminAsync"/>.</summary>
[ApiController]
[Route("api/llm-providers")]
public class LlmProvidersController(
    ProjectService projects, LlmModelCatalogService modelCatalog, ICurrentActor currentActor) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LlmProviderConfigDto>>> ListGlobal(CancellationToken ct)
    {
        // Managing website-wide providers is site-admin authority, and so is enumerating them — this is
        // the admin management list. Per-project provider selection uses the membership-gated project
        // endpoint (ListLlmProvidersAsync, which already merges globals), so non-admins don't need this.
        if (!currentActor.IsSiteAdmin)
        {
            throw new ForbiddenException("Only a site administrator can list website-wide LLM providers.");
        }
        return Ok(await projects.ListGlobalLlmProvidersAsync(ct));
    }

    [HttpPost]
    public async Task<ActionResult<LlmProviderConfigDto>> CreateGlobal(CreateLlmProviderRequest req, CancellationToken ct)
    {
        if (!currentActor.IsSiteAdmin)
        {
            throw new ForbiddenException("Only a site administrator can manage website-wide LLM providers.");
        }
        return Ok(await projects.CreateLlmProviderAsync(null, req, ct));
    }

    /// <summary>Discover the models an endpoint offers, for the website-wide provider form. POST because
    /// it carries a candidate configuration and makes an outbound call — it is a probe, not a fetch.
    /// Gated identically to creating a website-wide provider: this spends the referenced credential.</summary>
    [HttpPost("models")]
    public async Task<ActionResult<IReadOnlyList<LlmModelDto>>> ListModels(
        ListLlmModelsRequest req, CancellationToken ct)
    {
        if (!currentActor.IsSiteAdmin)
        {
            throw new ForbiddenException("Only a site administrator can manage website-wide LLM providers.");
        }
        return Ok(await modelCatalog.ListModelsAsync(req, ct));
    }

    // Update/delete are by id and work for both project-scoped and global configs.
    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<LlmProviderConfigDto>> Update(Guid id, UpdateLlmProviderRequest req, CancellationToken ct)
    {
        await projects.EnsureProviderAdminAsync(id, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        return Ok(await projects.UpdateLlmProviderAsync(id, req, ct));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await projects.EnsureProviderAdminAsync(id, currentActor.ActorId, currentActor.IsSiteAdmin, ct);
        await projects.DeleteLlmProviderAsync(id, ct);
        return NoContent();
    }
}
