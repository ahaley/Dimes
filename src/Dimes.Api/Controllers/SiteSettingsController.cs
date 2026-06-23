using Dimes.Api.Auth;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dimes.Api.Controllers;

/// <summary>Site branding: an anonymous read (the login screen needs the title before there's a session)
/// and a site-admin-only update.</summary>
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
}
