using Dimes.Api.Realtime;

namespace Dimes.Tests;

/// <summary>Records board notifications so tests can assert the realtime hook fired without a hub.</summary>
public sealed class FakeBoardNotifier : IBoardNotifier
{
    public List<(Guid ProjectId, Guid ChangeId, string Kind)> Events { get; } = new();
    public int ProjectsChangedCount { get; private set; }

    public Task ChangedAsync(Guid projectId, Guid changeId, string kind, CancellationToken ct = default)
    {
        Events.Add((projectId, changeId, kind));
        return Task.CompletedTask;
    }

    public Task ProjectsChangedAsync(CancellationToken ct = default)
    {
        ProjectsChangedCount++;
        return Task.CompletedTask;
    }
}
