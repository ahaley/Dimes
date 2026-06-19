using Dimes.Api.Auth;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dimes.Api.Controllers;

/// <summary>Site-admin-only user administration: list users, create local accounts, reset passwords,
/// and grant/revoke site-admin. Per-project membership stays under /api/projects/{id}/members.</summary>
[ApiController]
[Route("api/admin/users")]
[Authorize(DimesClaims.SiteAdminPolicy)]
public class UsersController(SiteAdminService admin) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SiteUserDto>>> List(CancellationToken ct)
        => Ok(await admin.ListUsersAsync(ct));

    [HttpPost]
    public async Task<ActionResult<SiteUserDto>> CreateLocal(CreateLocalUserRequest req, CancellationToken ct)
        => Ok(await admin.CreateLocalUserAsync(req, ct));

    [HttpPatch("{id:guid}")]
    public async Task<ActionResult<SiteUserDto>> Update(Guid id, UpdateActorRequest req, CancellationToken ct)
        => Ok(await admin.UpdateUserAsync(id, req, ct));

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await admin.ArchiveUserAsync(id, archived: true, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/unarchive")]
    public async Task<IActionResult> Unarchive(Guid id, CancellationToken ct)
    {
        await admin.ArchiveUserAsync(id, archived: false, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await admin.DeleteUserAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetPasswordRequest req, CancellationToken ct)
    {
        await admin.ResetPasswordAsync(id, req, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/site-admin")]
    public async Task<ActionResult<SiteUserDto>> SetSiteAdmin(Guid id, SetSiteAdminRequest req, CancellationToken ct)
        => Ok(await admin.SetSiteAdminAsync(id, req.IsSiteAdmin, ct));
}
