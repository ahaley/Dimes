using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Services;

/// <summary>One-time, idempotent seed/upgrade on startup: gives every project an editable copy of each
/// built-in system instruction (today just <see cref="SystemInstructionKind.ExportWorkOrder"/>), so
/// operators have a concrete row to edit rather than an invisible default. Also upgrades projects still
/// carrying an un-customized *older* default to the current text — an edit to a built-in default otherwise
/// only reaches new projects. A hand-edited row is never touched. Safe to run every boot.</summary>
public sealed class SystemInstructionBootstrapper(DimesDbContext db)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        // 1. Insert a row for any project that has none (created after a prior seed, or a fresh install).
        //    New rows get the current default, so they never need the upgrade in step 2.
        var projectIdsMissingExport = await db.Projects
            .Where(p => !db.SystemInstructions.Any(
                s => s.ProjectId == p.Id && s.Kind == SystemInstructionKind.ExportWorkOrder))
            .Select(p => p.Id)
            .ToListAsync(ct);
        foreach (var projectId in projectIdsMissingExport)
        {
            db.SystemInstructions.Add(new SystemInstruction
            {
                ProjectId = projectId,
                Kind = SystemInstructionKind.ExportWorkOrder,
                Content = SystemInstructionDefaults.ExportWorkOrder,
            });
        }

        // 2. Upgrade projects still carrying an un-customized older default to the current text. A row that
        //    was hand-edited matches no prior default and is left alone; a reset row has no entry at all
        //    (the renderer's fallback already yields the current default). Compared line-ending-tolerant so
        //    a CRLF/LF checkout difference doesn't defeat the match.
        var priors = SystemInstructionDefaults.PreviousExportWorkOrders.Select(Normalize).ToHashSet();
        var currentNormalized = Normalize(SystemInstructionDefaults.ExportWorkOrder);
        var exportRows = await db.SystemInstructions
            .Where(s => s.Kind == SystemInstructionKind.ExportWorkOrder)
            .ToListAsync(ct);
        foreach (var row in exportRows)
        {
            var normalized = Normalize(row.Content);
            if (normalized != currentNormalized && priors.Contains(normalized))
            {
                row.Content = SystemInstructionDefaults.ExportWorkOrder;
                row.UpdatedAt = DateTimeOffset.UtcNow;
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private static string Normalize(string content) => content.Replace("\r\n", "\n");
}
