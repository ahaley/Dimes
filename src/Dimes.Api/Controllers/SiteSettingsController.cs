using Dimes.Api.Auth;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dimes.Api.Controllers;

/// <summary>Site branding: an anonymous read (the login screen needs the title before there's a session)
/// and a site-admin-only update. Also the site project-creation policy, which is admin-only in both
/// directions — a non-admin's own effective allowance comes from <c>GET api/projects/quota</c> instead.</summary>
[ApiController]
public class SiteSettingsController(SiteSettingsService settings) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("api/config/branding")]
    public async Task<ActionResult<SiteBrandingDto>> GetBranding(CancellationToken ct)
        => Ok(await settings.GetAsync(ct));

    [Authorize(Policy = DimesClaims.SiteAdminPolicy)]
    [HttpPut("api/admin/branding")]
    public async Task<ActionResult<SiteBrandingDto>> UpdateBranding(UpdateSiteBrandingRequest req, CancellationToken ct)
        => Ok(await settings.UpdateAsync(req, ct));

    [Authorize(Policy = DimesClaims.SiteAdminPolicy)]
    [HttpGet("api/admin/project-policy")]
    public async Task<ActionResult<ProjectPolicyDto>> GetProjectPolicy(CancellationToken ct)
        => Ok(await settings.GetProjectPolicyAsync(ct));

    [Authorize(Policy = DimesClaims.SiteAdminPolicy)]
    [HttpPut("api/admin/project-policy")]
    public async Task<ActionResult<ProjectPolicyDto>> UpdateProjectPolicy(UpdateProjectPolicyRequest req, CancellationToken ct)
        => Ok(await settings.UpdateProjectPolicyAsync(req, ct));
}
