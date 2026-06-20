using Microsoft.AspNetCore.SignalR;

namespace Dimes.Api.Realtime;

/// <summary>Publishes board changes to subscribers of a project. Abstracted so application services
/// don't depend on SignalR directly (and so it's trivially fakeable in tests).</summary>
public interface IBoardNotifier
{
    Task ChangedAsync(Guid projectId, Guid changeId, string kind, CancellationToken ct = default);
}

public sealed class SignalRBoardNotifier(IHubContext<BoardHub> hub) : IBoardNotifier
{
    public Task ChangedAsync(Guid projectId, Guid changeId, string kind, CancellationToken ct = default)
        => hub.Clients.Group(BoardHub.Group(projectId))
            .SendAsync("boardChanged", new { projectId, changeId, kind }, ct);
}
