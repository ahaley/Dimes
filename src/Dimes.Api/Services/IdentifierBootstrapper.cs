using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Services;

/// <summary>One-time, idempotent backfill on startup: assigns a unique <c>Key</c> to every project that
/// lacks one (derived from its name), a per-project sequential <c>Number</c> (1..n, by creation order) to
/// every change that lacks one, and a <c>CompletedAt</c> to already-Done changes that predate that field.
/// New rows get these assigned at create/transition time; this only repairs pre-feature rows. Safe to run
/// every boot — it touches only rows where the value is still null.</summary>
public sealed class IdentifierBootstrapper(DimesDbContext db)
{
    public async Task BackfillAsync(CancellationToken ct = default)
    {
        var changed = await BackfillProjectKeysAsync(ct);
        changed |= await BackfillChangeNumbersAsync(ct);
        changed |= await BackfillCompletedAtAsync(ct);
        if (changed)
        {
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Stamp a <c>CompletedAt</c> on Done changes created before the field existed, using their
    /// last-updated time as the best available proxy for when they were accepted.</summary>
    private async Task<bool> BackfillCompletedAtAsync(CancellationToken ct)
    {
        var doneWithout = await db.ChangeRequests
            .Where(c => c.Status == Dimes.Domain.ChangeStatus.Done && c.CompletedAt == null)
            .ToListAsync(ct);
        if (doneWithout.Count == 0)
        {
            return false;
        }
        foreach (var change in doneWithout)
        {
            change.CompletedAt = change.UpdatedAt;
        }
        return true;
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
