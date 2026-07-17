using System.Text;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Services;

/// <summary>Builds the daily per-actor digest. Once a day (or on a short test interval) it walks every
/// project that has an enabled channel subscribed to <see cref="NotificationEventType.DailyDigest"/> and
/// enqueues one digest message per project, sectioned per human member — "Alice — 3 changes await your
/// approval · 2 assigned to you" — with opted-out actors excluded. Delivery goes to the project's Google
/// Chat space (per-actor DMs are a deferred enhancement). It only enqueues; the drain worker delivers.</summary>
public sealed class NotificationDigestService(
    IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<NotificationDigestService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Test/observability hook: when Notifications:DigestIntervalSeconds is set, run on that cadence
        // instead of the daily schedule, so a digest can be triggered on demand during verification.
        var intervalSeconds = configuration.GetValue<int?>("Notifications:DigestIntervalSeconds");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = intervalSeconds is > 0
                    ? TimeSpan.FromSeconds(intervalSeconds.Value)
                    : DelayUntilNextRun();
                await Task.Delay(delay, stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Digest build failed; will retry next run.");
            }
        }
    }

    /// <summary>Time until the next configured digest hour (UTC; default 08:00).</summary>
    private TimeSpan DelayUntilNextRun()
    {
        var hour = Math.Clamp(configuration.GetValue("Notifications:DigestHourUtc", 8), 0, 23);
        var now = DateTimeOffset.UtcNow;
        var next = new DateTimeOffset(now.Year, now.Month, now.Day, hour, 0, 0, TimeSpan.Zero);
        if (next <= now)
        {
            next = next.AddDays(1);
        }
        return next - now;
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DimesDbContext>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<INotificationDispatcher>();

        // Projects with at least one enabled channel subscribed to the digest.
        var enabledChannels = await db.NotificationChannelConfigs
            .Where(c => c.Enabled)
            .ToListAsync(ct);
        var projectIds = enabledChannels
            .Where(c => NotificationDispatcher.Subscribes(c, NotificationEventType.DailyDigest))
            .Select(c => c.ProjectId)
            .Distinct()
            .ToList();

        foreach (var projectId in projectIds)
        {
            var (title, body) = await BuildDigestAsync(db, projectId, ct);
            if (body is null)
            {
                continue; // nothing worth sending for this project today
            }
            await dispatcher.EnqueueAsync(projectId, NotificationEventType.DailyDigest, title, body, ct: ct);
            await db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Compose one project's digest. Returns a null body when there's nothing to report (no actor
    /// has pending approvals or assignments and the inbox didn't grow), so an empty digest is never sent.</summary>
    private static async Task<(string Title, string? Body)> BuildDigestAsync(
        DimesDbContext db, Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects.FindAsync([projectId], ct);
        if (project is null)
        {
            return ("Daily digest", null);
        }

        var title = $"Daily digest — {project.Name}";

        var members = await db.Memberships
            .Where(m => m.ProjectId == projectId && m.Actor.Type == ActorType.Human && !m.Actor.IsArchived)
            .Include(m => m.Actor)
            .ToListAsync(ct);

        // Effective opt-out: a project-scoped preference wins over the actor's global one.
        var actorIds = members.Select(m => m.ActorId).ToList();
        var prefs = await db.NotificationPreferences
            .Where(p => actorIds.Contains(p.ActorId) && (p.ProjectId == projectId || p.ProjectId == null))
            .ToListAsync(ct);
        bool OptedOut(Guid actorId)
        {
            var scoped = prefs.FirstOrDefault(p => p.ActorId == actorId && p.ProjectId == projectId);
            if (scoped is not null)
            {
                return scoped.DigestOptOut;
            }
            return prefs.FirstOrDefault(p => p.ActorId == actorId && p.ProjectId == null)?.DigestOptOut ?? false;
        }

        var awaitingApproval = await db.ChangeRequests
            .CountAsync(c => c.ProjectId == projectId && c.Status == ChangeStatus.Triaged, ct);

        var assignmentCounts = (await db.ChangeRequests
            .Where(c => c.ProjectId == projectId && c.AssigneeActorId != null
                && c.Status != ChangeStatus.Done
                && c.Status != ChangeStatus.Rejected
                && c.Status != ChangeStatus.Duplicate)
            .GroupBy(c => c.AssigneeActorId!.Value)
            .Select(g => new { ActorId = g.Key, Count = g.Count() })
            .ToListAsync(ct))
            .ToDictionary(x => x.ActorId, x => x.Count);

        var cutoff = DateTimeOffset.UtcNow.AddDays(-1);
        var inboxGrowth = await db.Observations
            .CountAsync(o => o.ProjectId == projectId && o.Status == ObservationStatus.New && o.CreatedAt >= cutoff, ct);

        var lines = new List<string>();
        foreach (var member in members.OrderBy(m => m.Actor.DisplayName))
        {
            if (OptedOut(member.ActorId))
            {
                continue;
            }

            var approvals = member.Role == MemberRole.Maintainer ? awaitingApproval : 0;
            var assigned = assignmentCounts.TryGetValue(member.ActorId, out var n) ? n : 0;
            if (approvals == 0 && assigned == 0)
            {
                continue;
            }

            var parts = new List<string>();
            if (approvals > 0)
            {
                parts.Add($"{approvals} change{Plural(approvals)} await{(approvals == 1 ? "s" : "")} your approval");
            }
            if (assigned > 0)
            {
                parts.Add($"{assigned} assigned to you");
            }
            lines.Add($"• {member.Actor.DisplayName} — {string.Join(" · ", parts)}");
        }

        if (lines.Count == 0 && inboxGrowth == 0)
        {
            return (title, null);
        }

        var sb = new StringBuilder();
        if (inboxGrowth > 0)
        {
            sb.AppendLine($"Inbox grew by {inboxGrowth} new observation{Plural(inboxGrowth)} in the last 24h.");
            if (lines.Count > 0)
            {
                sb.AppendLine();
            }
        }
        foreach (var line in lines)
        {
            sb.AppendLine(line);
        }
        return (title, sb.ToString().TrimEnd());
    }

    private static string Plural(int n) => n == 1 ? "" : "s";
}
