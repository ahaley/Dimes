using Dimes.Api.Contracts;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Domain.Providers;
using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Services;

/// <summary>Links a change to a source-control item and pulls read-only context for it. Context
/// fetching is best-effort: a private repo without a configured token, an unreachable host, or an
/// unrecognized URL simply yields a link with no snapshot rather than failing the request.</summary>
public class ScmService(DimesDbContext db, IEnumerable<IScmProvider> providers, ISecretResolver secrets)
{
    public async Task<ScmLinkDto> AddLinkAsync(Guid changeId, AddScmLinkRequest req, CancellationToken ct = default)
    {
        var change = await db.ChangeRequests.FindAsync([changeId], ct)
            ?? throw new NotFoundException($"Change request '{changeId}' not found.");

        if (string.IsNullOrWhiteSpace(req.Url))
        {
            throw new BadRequestException("SCM link URL is required.");
        }

        // Explicit snapshot wins; otherwise try to pull context from the provider.
        var snapshot = req.ContextSnapshot;
        if (snapshot is null)
        {
            var provider = providers.FirstOrDefault(p => p.Type == ScmProviderType.GitHub);
            if (provider is not null)
            {
                var config = await db.ScmProviderConfigs.FirstOrDefaultAsync(
                    c => c.ProjectId == change.ProjectId && c.Type == ScmProviderType.GitHub, ct);
                var token = secrets.Resolve(config?.TokenSecretRef);
                try
                {
                    var context = await provider.FetchContextAsync(req.Url, token, ct);
                    snapshot = context?.Raw;
                }
                catch
                {
                    // Best-effort: leave snapshot null on any fetch failure.
                }
            }
        }

        var link = new ScmLink
        {
            ChangeRequestId = change.Id,
            Provider = ScmProviderType.GitHub,
            Url = req.Url,
            ContextSnapshot = snapshot,
        };
        db.ScmLinks.Add(link);
        await db.SaveChangesAsync(ct);
        return link.ToDto();
    }
}
