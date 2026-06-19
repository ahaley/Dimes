using System.Security.Claims;
using Dimes.Domain.Entities;
using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Auth;

/// <summary>The authenticated actor for the current request, derived from the session principal.
/// This is the auth-aware replacement for the old <c>ActorId</c> request field: controllers read
/// <see cref="ActorId"/> and pass it into services, which still authorize via MembershipResolver.</summary>
public interface ICurrentActor
{
    bool IsAuthenticated { get; }

    /// <summary>The acting actor's id from the session. Throws <see cref="UnauthorizedException"/>
    /// if there is no authenticated actor (defensive — the fallback policy normally returns 401 first).</summary>
    Guid ActorId { get; }

    bool IsSiteAdmin { get; }

    /// <summary>Load the full <see cref="Actor"/> row for the current session.</summary>
    Task<Actor> GetAsync(CancellationToken ct = default);
}

public sealed class CurrentActor(IHttpContextAccessor accessor, DimesDbContext db) : ICurrentActor
{
    private ClaimsPrincipal? User => accessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public Guid ActorId =>
        Guid.TryParse(User?.FindFirst(DimesClaims.ActorId)?.Value, out var id)
            ? id
            : throw new UnauthorizedException("No authenticated actor on the request.");

    public bool IsSiteAdmin => User?.HasClaim(DimesClaims.SiteAdmin, "true") ?? false;

    public async Task<Actor> GetAsync(CancellationToken ct = default)
    {
        var id = ActorId;
        return await db.Actors.FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new UnauthorizedException("The authenticated actor no longer exists.");
    }
}
