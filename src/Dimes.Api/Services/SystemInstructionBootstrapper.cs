using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Services;

/// <summary>One-time, idempotent seed on startup: gives every project an editable copy of each built-in
/// system instruction (today just <see cref="SystemInstructionKind.ExportWorkOrder"/>), so operators have a
/// concrete row to edit rather than an invisible default. Projects created later rely on the renderer's
/// built-in fallback until someone customizes them. Safe to run every boot — it only inserts rows for
/// (project, kind) pairs that don't already exist.</summary>
public sealed class SystemInstructionBootstrapper(DimesDbContext db)
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var projectIdsMissingExport = await db.Projects
            .Where(p => !db.SystemInstructions.Any(
                s => s.ProjectId == p.Id && s.Kind == SystemInstructionKind.ExportWorkOrder))
            .Select(p => p.Id)
            .ToListAsync(ct);
        if (projectIdsMissingExport.Count == 0)
        {
            return;
        }

        foreach (var projectId in projectIdsMissingExport)
        {
            db.SystemInstructions.Add(new SystemInstruction
            {
                ProjectId = projectId,
                Kind = SystemInstructionKind.ExportWorkOrder,
                Content = SystemInstructionDefaults.ExportWorkOrder,
            });
        }
        await db.SaveChangesAsync(ct);
    }
}
