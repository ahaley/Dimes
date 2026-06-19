using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Services;

/// <summary>Resolves an actor and their role within a project, enforcing that the actor is a member.
/// The resolved role is what the lifecycle engine checks its guards against.</summary>
public class MembershipResolver(DimesDbContext db)
{
    public async Task<(Actor Actor, MemberRole Role)> ResolveAsync(
        Guid projectId, Guid actorId, CancellationToken ct = default)
    {
        var membership = await db.Memberships
            .Include(m => m.Actor)
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.ActorId == actorId, ct);

        if (membership is null)
        {
            throw new ForbiddenException($"Actor '{actorId}' is not a member of project '{projectId}'.");
        }

        return (membership.Actor, membership.Role);
    }
}
