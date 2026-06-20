using Dimes.Api.Auth;
using Dimes.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Realtime;

/// <summary>Realtime board updates. Clients call <see cref="JoinProject"/> to subscribe to a project's
/// group; membership (or site-admin) is verified before joining so you only receive events for
/// projects you can access. Authenticated (cookie) — the connection principal carries the Dimes claims.</summary>
[Authorize]
public class BoardHub(DimesDbContext db) : Hub
{
    public static string Group(Guid projectId) => $"project:{projectId}";

    public async Task JoinProject(Guid projectId)
    {
        var user = Context.User;
        var isSiteAdmin = user?.HasClaim(DimesClaims.SiteAdmin, "true") ?? false;
        Guid.TryParse(user?.FindFirst(DimesClaims.ActorId)?.Value, out var actorId);

        var allowed = isSiteAdmin
            || (actorId != Guid.Empty
                && await db.Memberships.AnyAsync(m => m.ProjectId == projectId && m.ActorId == actorId));
        if (!allowed)
        {
            throw new HubException("Not a member of this project.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, Group(projectId));
    }

    public Task LeaveProject(Guid projectId)
        => Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(projectId));
}
