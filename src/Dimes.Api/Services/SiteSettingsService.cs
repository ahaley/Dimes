using Dimes.Api.Contracts;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Services;

/// <summary>Reads and updates the single-row site settings (currently just the brand title). Reads are
/// public (the title shows on the login screen, before auth); updates are site-admin only — enforced by
/// the controller's policy.</summary>
public class SiteSettingsService(DimesDbContext db)
{
    public const int MaxTitleLength = 60;

    /// <summary>The configured site title, or the default when unset/blank.</summary>
    public async Task<SiteBrandingDto> GetAsync(CancellationToken ct = default)
    {
        var title = await db.SiteSettings.AsNoTracking().Select(s => s.Title).FirstOrDefaultAsync(ct);
        return new SiteBrandingDto(string.IsNullOrWhiteSpace(title) ? SiteSettings.DefaultTitle : title);
    }

    /// <summary>Set the site title. Find-or-create the single row; trims and validates length.</summary>
    public async Task<SiteBrandingDto> UpdateAsync(UpdateSiteBrandingRequest req, CancellationToken ct = default)
    {
        var title = req.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new BadRequestException("Site title is required.");
        }
        if (title.Length > MaxTitleLength)
        {
            throw new BadRequestException($"Site title must be {MaxTitleLength} characters or fewer.");
        }

        var row = await db.SiteSettings.FirstOrDefaultAsync(ct);
        if (row is null)
        {
            db.SiteSettings.Add(new SiteSettings { Title = title });
        }
        else
        {
            row.Title = title;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return new SiteBrandingDto(title);
    }

    /// <summary>The site-wide project-creation default, or the built-in default when no row exists yet.
    /// A user's personal <see cref="Actor.ProjectLimit"/> overrides this; site admins ignore both.</summary>
    public async Task<ProjectPolicyDto> GetProjectPolicyAsync(CancellationToken ct = default)
    {
        var limit = await db.SiteSettings.AsNoTracking().Select(s => (int?)s.ProjectLimit).FirstOrDefaultAsync(ct);
        return new ProjectPolicyDto(limit ?? SiteSettings.DefaultProjectLimit);
    }

    /// <summary>Set how many projects a non-admin may create by default. 0 restricts creation to site
    /// administrators. Find-or-create the single row, matching <see cref="UpdateAsync"/>.</summary>
    public async Task<ProjectPolicyDto> UpdateProjectPolicyAsync(
        UpdateProjectPolicyRequest req, CancellationToken ct = default)
    {
        ValidateProjectLimit(req.ProjectLimit);

        var row = await db.SiteSettings.FirstOrDefaultAsync(ct);
        if (row is null)
        {
            db.SiteSettings.Add(new SiteSettings { ProjectLimit = req.ProjectLimit });
        }
        else
        {
            row.ProjectLimit = req.ProjectLimit;
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return new ProjectPolicyDto(req.ProjectLimit);
    }

    /// <summary>Shared range check for both the site default and a per-user override.</summary>
    public static void ValidateProjectLimit(int limit)
    {
        if (limit < 0)
        {
            throw new BadRequestException("A project limit can't be negative. Use 0 to disallow creation.");
        }
        if (limit > SiteSettings.MaxProjectLimit)
        {
            throw new BadRequestException($"A project limit must be {SiteSettings.MaxProjectLimit} or fewer.");
        }
    }
}
