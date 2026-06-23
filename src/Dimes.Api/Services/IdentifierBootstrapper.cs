using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Services;

/// <summary>One-time, idempotent backfill of human-readable identifiers on startup: assigns a unique
/// <c>Key</c> to every project that lacks one (derived from its name) and a per-project sequential
/// <c>Number</c> (1..n, by creation order) to every change that lacks one. New projects/changes get
/// these assigned at create time; this only repairs rows that predate the feature. Safe to run every
/// boot — it touches only rows where the value is still null.</summary>
public sealed class IdentifierBootstrapper(DimesDbContext db)
{
    public async Task BackfillAsync(CancellationToken ct = default)
    {
        var changed = await BackfillProjectKeysAsync(ct);
        changed |= await BackfillChangeNumbersAsync(ct);
        if (changed)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<bool> BackfillProjectKeysAsync(CancellationToken ct)
    {
        var keyless = await db.Projects.Where(p => p.Key == null).OrderBy(p => p.CreatedAt).ToListAsync(ct);
        if (keyless.Count == 0)
        {
            return false;
        }

        // Seed the taken-set from keys already in use so derived keys stay globally unique.
        var taken = (await db.Projects.Where(p => p.Key != null).Select(p => p.Key!).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var project in keyless)
        {
            var key = ProjectKeys.DeriveUnique(project.Name, taken);
            project.Key = key;
            taken.Add(key);
        }
        return true;
    }

    private async Task<bool> BackfillChangeNumbersAsync(CancellationToken ct)
    {
        // Projects that have any unnumbered change.
        var projectIds = await db.ChangeRequests
            .Where(c => c.Number == null)
            .Select(c => c.ProjectId)
            .Distinct()
            .ToListAsync(ct);
        if (projectIds.Count == 0)
        {
            return false;
        }

        foreach (var projectId in projectIds)
        {
            var next = (await db.ChangeRequests
                .Where(c => c.ProjectId == projectId && c.Number != null)
                .MaxAsync(c => (int?)c.Number, ct) ?? 0) + 1;

            var unnumbered = await db.ChangeRequests
                .Where(c => c.ProjectId == projectId && c.Number == null)
                .OrderBy(c => c.CreatedAt)
                .ToListAsync(ct);
            foreach (var change in unnumbered)
            {
                change.Number = next++;
            }
        }
        return true;
    }
}
