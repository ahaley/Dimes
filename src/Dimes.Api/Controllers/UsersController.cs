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
