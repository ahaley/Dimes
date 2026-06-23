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
}
