using Microsoft.AspNetCore.SignalR;

namespace Dimes.Api.Realtime;

/// <summary>Publishes board changes to subscribers of a project. Abstracted so application services
/// don't depend on SignalR directly (and so it's trivially fakeable in tests).</summary>
public interface IBoardNotifier
{
    Task ChangedAsync(Guid projectId, Guid changeId, string kind, CancellationToken ct = default);

    /// <summary>The set of projects changed (created / archived / unarchived). Broadcast to every
    /// connected client so each refreshes its (membership-filtered) sidebar list — not scoped to a
    /// project group, since the change is about which projects exist at all.</summary>
    Task ProjectsChangedAsync(CancellationToken ct = default);
}

public sealed class SignalRBoardNotifier(IHubContext<BoardHub> hub) : IBoardNotifier
{
    public Task ChangedAsync(Guid projectId, Guid changeId, string kind, CancellationToken ct = default)
        => hub.Clients.Group(BoardHub.Group(projectId))
            .SendAsync("boardChanged", new { projectId, changeId, kind }, ct);

    public Task ProjectsChangedAsync(CancellationToken ct = default)
        => hub.Clients.All.SendAsync("projectsChanged", new { }, ct);
}
