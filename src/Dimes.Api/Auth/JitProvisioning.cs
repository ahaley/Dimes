using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Auth;

/// <summary>Just-in-time provisioning for OIDC logins: the first time an external identity signs in,
/// create a Human <see cref="Actor"/> from its claims. The actor gets NO project membership — a
/// Maintainer must grant access — so existing per-project RBAC is unchanged. Host-free (takes a
/// DbContext) so it is unit-testable without an HTTP pipeline.</summary>
public static class JitProvisioning
{
    public static async Task<Actor> ProvisionAsync(
        DimesDbContext db, string email, string? displayName, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();

        var actor = await db.Actors
            .FirstOrDefaultAsync(a => a.Email != null && a.Email.ToLower() == normalized, ct);
        if (actor is not null)
        {
            return actor;
        }

        actor = new Actor
        {
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? normalized : displayName.Trim(),
            Type = ActorType.Human,
            Email = normalized,
        };
        db.Actors.Add(actor);
        await db.SaveChangesAsync(ct);
        return actor;
    }
}
