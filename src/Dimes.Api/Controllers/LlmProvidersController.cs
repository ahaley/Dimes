using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Dimes.Api.Controllers;

/// <summary>Website-wide (global) LLM providers — available to every project. Per-project providers
/// live under <c>/api/projects/{id}/llm-providers</c>.</summary>
[ApiController]
[Route("api/llm-providers")]
public class LlmProvidersController(ProjectService projects) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<LlmProviderConfigDto>>> ListGlobal(CancellationToken ct)
        => Ok(await projects.ListGlobalLlmProvidersAsync(ct));

    [HttpPost]
    public async Task<ActionResult<LlmProviderConfigDto>> CreateGlobal(CreateLlmProviderRequest req, CancellationToken ct)
        => Ok(await projects.CreateLlmProviderAsync(null, req, ct));

    // Update/delete are by id and work for both project-scoped and global configs.
    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<LlmProviderConfigDto>> Update(Guid id, UpdateLlmProviderRequest req, CancellationToken ct)
        => Ok(await projects.UpdateLlmProviderAsync(id, req, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await projects.DeleteLlmProviderAsync(id, ct);
        return NoContent();
    }
}
