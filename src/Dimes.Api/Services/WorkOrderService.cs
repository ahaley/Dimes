using System.Text;
using Dimes.Api.Contracts;
using Dimes.Api.Realtime;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Domain.WorkOrders;
using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Services;

/// <summary>Ingests an agent's report against an exported work order, closing the loop the export opened.
/// Strictly recommend-only: this attaches links, posts a summary comment, and flags the item as reported —
/// it never changes a change's status. A human confirms InDevelopment → InReview through the normal guarded
/// <c>LifecycleService</c> path, which is also where the item settles to Confirmed.</summary>
public class WorkOrderService(
    DimesDbContext db, MembershipResolver members, IBoardNotifier notifier, INotificationDispatcher notifications)
{
    // The report endpoint is anonymous — the token is the only gate — so bound what one call can store.
    // Mirrors the capture endpoint's posture rather than trusting a well-behaved client.
    private const int MaxCommits = 200;
    private const int MaxPullRequests = 50;
    private const int MaxBlocked = 50;
    private const int MaxMessageLength = 8 * 1024;
    private const int MaxNoteLength = 2 * 1024;

    /// <summary>Resolve a report against its work order and apply it. Returns 200-shaped results whenever
    /// the token is valid, even if nothing matched, so a retrying agent settles instead of looping.</summary>
    public async Task<WorkOrderResultsDto> ReportResultsAsync(
        string token, WorkOrderResultsRequest req, CancellationToken ct = default)
    {
        var commits = req.Commits ?? [];
        var pullRequests = req.PullRequests ?? [];
        var blocked = req.Blocked ?? [];
        ValidateCaps(commits, pullRequests, blocked);

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new NotFoundException("Work order not found or expired.");
        }

        var hash = WorkOrderToken.Hash(token);
        var workOrder = await db.WorkOrders
            .Include(w => w.Items).ThenInclude(i => i.ChangeRequest)
            .Include(w => w.ExportedBy)
            .FirstOrDefaultAsync(w => w.TokenHash == hash, ct);

        // One message for "no such token" and "expired token" alike: a distinct expiry error would confirm
        // to a holder of a leaked-but-stale token that it was once real.
        if (workOrder is null || workOrder.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            throw new NotFoundException("Work order not found or expired.");
        }

        // The token carries the exporting actor's authority and nothing more, re-checked on every report:
        // removing that member from the project revokes their outstanding work orders.
        var (actor, _) = await members.ResolveAsync(workOrder.ProjectId, workOrder.ExportedByActorId, ct);

        var project = await db.Projects.FindAsync([workOrder.ProjectId], ct);
        var now = DateTimeOffset.UtcNow;
        var ignored = new List<string>();
        var reports = new Dictionary<Guid, ItemReport>();

        ItemReport ReportFor(WorkOrderItem item)
        {
            if (!reports.TryGetValue(item.ChangeRequestId, out var report))
            {
                report = new ItemReport(item);
                reports[item.ChangeRequestId] = report;
            }
            return report;
        }

        foreach (var commit in commits)
        {
            var (matches, inferred) = ResolveCommit(workOrder, commit, ignored);
            foreach (var item in matches)
            {
                ReportFor(item).Commits.Add((commit, inferred));
            }
        }

        foreach (var pr in pullRequests)
        {
            var item = ResolvePullRequest(workOrder, pr, ignored);
            if (item is not null)
            {
                ReportFor(item).PullRequests.Add(pr);
            }
        }

        // Existing links, loaded once: re-sending a report must not duplicate them.
        var touchedIds = reports.Keys.ToList();
        var existingUrls = await db.ScmLinks
            .Where(l => touchedIds.Contains(l.ChangeRequestId))
            .Select(l => new { l.ChangeRequestId, l.Url })
            .ToListAsync(ct);
        var linkedUrls = existingUrls
            .GroupBy(l => l.ChangeRequestId)
            .ToDictionary(g => g.Key, g => g.Select(l => l.Url).ToHashSet(StringComparer.OrdinalIgnoreCase));

        var summary = Truncate(req.Summary, MaxNoteLength);
        var touched = new HashSet<Guid>();

        foreach (var report in reports.Values)
        {
            var item = report.Item;
            var urls = linkedUrls.TryGetValue(item.ChangeRequestId, out var known)
                ? known
                : linkedUrls[item.ChangeRequestId] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var added = 0;
            foreach (var url in report.LinkUrls())
            {
                // Skip rather than throw: one malformed URL shouldn't reject an agent's whole run.
                if (!ScmUrlValidator.IsValid(url) || !urls.Add(url))
                {
                    continue;
                }
                db.ScmLinks.Add(new ScmLink
                {
                    ChangeRequestId = item.ChangeRequestId,
                    Provider = ScmProviderType.GitHub,
                    Url = url,
                });
                added++;
            }
            report.LinksAdded = added;

            var statusChanged = item.Status != WorkOrderItemStatus.Reported;
            // Comment only when the report actually told us something new. This is what makes a retry a
            // true no-op — an identical replay adds no links and the item is already Reported — while a
            // follow-up carrying genuinely new commits still gets recorded.
            if (added > 0 || statusChanged)
            {
                db.Comments.Add(new Comment
                {
                    ChangeRequestId = item.ChangeRequestId,
                    AuthorActorId = actor.Id,
                    Kind = CommentKind.AgentRecommendation,
                    Body = BuildReportBody(workOrder, actor, report, summary, now),
                });
                touched.Add(item.ChangeRequestId);
            }

            item.Status = WorkOrderItemStatus.Reported;
            item.ReportedAt = now;
            item.ReportNote = summary;
        }

        // Applied after commits so that an agent which committed partial work and then gave up ends up
        // Blocked: the human needs to see the ask, not an InReview prompt.
        foreach (var entry in blocked)
        {
            var item = workOrder.Items.FirstOrDefault(i => i.ChangeRequestId == entry.ChangeId);
            if (item is null)
            {
                ignored.Add($"Blocked report for change {entry.ChangeId} is not part of this work order.");
                continue;
            }

            var reason = Truncate(entry.Reason, MaxNoteLength);
            var alreadyBlockedForSameReason =
                item.Status == WorkOrderItemStatus.Blocked && item.ReportNote == reason;
            if (!alreadyBlockedForSameReason)
            {
                db.Comments.Add(new Comment
                {
                    ChangeRequestId = item.ChangeRequestId,
                    AuthorActorId = actor.Id,
                    Kind = CommentKind.AgentRecommendation,
                    Body = BuildBlockedBody(workOrder, actor, reason, now),
                });
                touched.Add(item.ChangeRequestId);
            }

            item.Status = WorkOrderItemStatus.Blocked;
            item.ReportedAt = now;
            item.ReportNote = reason;
            if (reports.TryGetValue(item.ChangeRequestId, out var existing))
            {
                existing.Blocked = true;
            }
        }

        workOrder.LastReportedAt = now;

        // Notify the human who exported this work order that an agent reported back — but only when the
        // report actually did something (a replayed/empty report touches nothing and stays silent).
        if (touched.Count > 0)
        {
            var reported = workOrder.Items.Count(
                i => i.Status is WorkOrderItemStatus.Reported or WorkOrderItemStatus.Confirmed);
            var blockedCount = workOrder.Items.Count(i => i.Status == WorkOrderItemStatus.Blocked);
            await notifications.EnqueueAsync(
                workOrder.ProjectId, NotificationEventType.WorkOrderResults, "Work order results received",
                $"Your work order \"{workOrder.FileName}\" received an agent report — {reported} reported, {blockedCount} blocked.",
                recipientActorId: workOrder.ExportedByActorId, ct: ct);
        }

        await db.SaveChangesAsync(ct);

        foreach (var changeId in touched)
        {
            await notifier.ChangedAsync(workOrder.ProjectId, changeId, "reported", ct);
        }

        return BuildResults(workOrder, project?.Key, reports, ignored);
    }

    /// <summary>The project's most recent export and how far it has come back — the tracking strip and the
    /// re-export warning. Null when the project has never exported.</summary>
    public async Task<WorkOrderSummaryDto?> LatestAsync(Guid projectId, CancellationToken ct = default)
    {
        var workOrder = await db.WorkOrders
            .Include(w => w.Items)
            .Where(w => w.ProjectId == projectId)
            .OrderByDescending(w => w.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (workOrder is null)
        {
            return null;
        }

        return new WorkOrderSummaryDto(
            workOrder.Id,
            workOrder.FileName,
            workOrder.CreatedAt,
            workOrder.ExportedByActorId,
            workOrder.Items.Count,
            workOrder.Items.Count(i => i.Status is WorkOrderItemStatus.Reported or WorkOrderItemStatus.Confirmed),
            workOrder.Items.Count(i => i.Status == WorkOrderItemStatus.Blocked),
            workOrder.Items.Where(i => i.Status == WorkOrderItemStatus.Pending)
                .Select(i => i.ChangeRequestId).ToList());
    }

    private static void ValidateCaps(
        IReadOnlyList<WorkOrderCommitReport> commits,
        IReadOnlyList<WorkOrderPullRequestReport> pullRequests,
        IReadOnlyList<WorkOrderBlockedReport> blocked)
    {
        if (commits.Count > MaxCommits)
        {
            throw new BadRequestException($"A report carries at most {MaxCommits} commits.");
        }
        if (pullRequests.Count > MaxPullRequests)
        {
            throw new BadRequestException($"A report carries at most {MaxPullRequests} pull requests.");
        }
        if (blocked.Count > MaxBlocked)
        {
            throw new BadRequestException($"A report carries at most {MaxBlocked} blocked entries.");
        }
    }

    /// <summary>Resolve one commit to the items it claims. A full-GUID trailer is authoritative; only if a
    /// commit carries none do we fall back to its branch, and only ever within this work order — honoring a
    /// trailer for a change outside it would silently widen a per-export token into a project-wide one.</summary>
    private static (List<WorkOrderItem> Matches, bool Inferred) ResolveCommit(
        WorkOrder workOrder, WorkOrderCommitReport commit, List<string> ignored)
    {
        var matches = new List<WorkOrderItem>();
        var claimed = WorkOrderTrailer.ParseTrailers(Truncate(commit.Message, MaxMessageLength));
        foreach (var id in claimed)
        {
            var item = workOrder.Items.FirstOrDefault(i => i.ChangeRequestId == id);
            if (item is not null)
            {
                matches.Add(item);
            }
            else
            {
                ignored.Add(
                    $"Commit {Short(commit.Sha)} names change {id}, which is not part of this work order.");
            }
        }
        if (matches.Count > 0 || claimed.Count > 0)
        {
            // The commit made a claim. If none of it landed, don't second-guess it with its branch.
            return (matches, false);
        }

        var byBranch = ResolveBranch(workOrder, commit.Branch);
        if (byBranch is not null)
        {
            return ([byBranch], true);
        }

        if (!string.IsNullOrWhiteSpace(commit.Branch) || !string.IsNullOrWhiteSpace(commit.Sha))
        {
            ignored.Add(
                $"Commit {Short(commit.Sha)} carries no `Dimes change <id>` trailer and its branch " +
                $"'{commit.Branch}' doesn't match a change in this work order.");
        }
        return (matches, false);
    }

    private static WorkOrderItem? ResolvePullRequest(
        WorkOrder workOrder, WorkOrderPullRequestReport pr, List<string> ignored)
    {
        if (pr.ChangeId is Guid id)
        {
            var item = workOrder.Items.FirstOrDefault(i => i.ChangeRequestId == id);
            if (item is null)
            {
                ignored.Add($"Pull request {pr.Url} names change {id}, which is not part of this work order.");
            }
            return item;
        }

        var byBranch = ResolveBranch(workOrder, pr.Branch);
        if (byBranch is null)
        {
            ignored.Add($"Pull request {pr.Url} couldn't be matched to a change in this work order.");
        }
        return byBranch;
    }

    /// <summary>Match a branch to an item: first by exact name — we minted and stored that string at export,
    /// so it's an equality check, not a heuristic — then, as a last resort, by the 8-hex id prefix, and only
    /// when exactly one item matches. An id8 is not an identity.</summary>
    private static WorkOrderItem? ResolveBranch(WorkOrder workOrder, string? branch)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            return null;
        }

        var exact = workOrder.Items.FirstOrDefault(
            i => string.Equals(i.BranchName, branch.Trim(), StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return exact;
        }

        var prefix = WorkOrderTrailer.ParseBranchIdPrefix(branch);
        if (prefix is null)
        {
            return null;
        }

        var candidates = workOrder.Items
            .Where(i => i.ChangeRequestId.ToString().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return candidates.Count == 1 ? candidates[0] : null;
    }

    private static string BuildReportBody(
        WorkOrder workOrder, Actor actor, ItemReport report, string? summary, DateTimeOffset now)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Provenance(workOrder, actor, now));
        if (!string.IsNullOrWhiteSpace(summary))
        {
            sb.AppendLine();
            sb.AppendLine(summary);
        }
        sb.AppendLine();
        foreach (var (commit, inferred) in report.Commits)
        {
            var subject = FirstLine(commit.Message);
            var line = $"- `{Short(commit.Sha)}` — {subject}";
            if (inferred)
            {
                // Say so: a branch-matched claim is weaker evidence than a trailer, and the human about to
                // confirm the transition is the one who should judge that.
                line += $" _(matched by branch `{commit.Branch}`, no trailer)_";
            }
            sb.AppendLine(line);
        }
        foreach (var pr in report.PullRequests)
        {
            sb.AppendLine($"- PR: {pr.Url}");
        }
        return sb.ToString().TrimEnd();
    }

    private static string BuildBlockedBody(WorkOrder workOrder, Actor actor, string reason, DateTimeOffset now)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Provenance(workOrder, actor, now));
        sb.AppendLine();
        sb.AppendLine($"**Reported blocked:** {reason}");
        return sb.ToString().TrimEnd();
    }

    private static string Provenance(WorkOrder workOrder, Actor actor, DateTimeOffset now) =>
        $"Agent report for work order `{workOrder.FileName}` " +
        $"(exported by {actor.DisplayName}, reported {now:yyyy-MM-dd HH:mm} UTC).";

    private static WorkOrderResultsDto BuildResults(
        WorkOrder workOrder, string? projectKey, Dictionary<Guid, ItemReport> reports, List<string> ignored)
    {
        var items = workOrder.Items.Select(i => new WorkOrderResultItemDto(
            i.ChangeRequestId,
            projectKey != null && i.ChangeRequest?.Number is int n ? $"{projectKey}-{n}" : null,
            i.TitleSnapshot,
            i.Status,
            reports.TryGetValue(i.ChangeRequestId, out var r) ? r.LinksAdded : 0)).ToList();

        return new WorkOrderResultsDto(
            workOrder.Id,
            items.Count,
            items.Count(i => i.Status is WorkOrderItemStatus.Reported or WorkOrderItemStatus.Confirmed),
            items.Count(i => i.Status == WorkOrderItemStatus.Blocked),
            items.Count(i => i.Status == WorkOrderItemStatus.Pending),
            items,
            ignored);
    }

    private static string FirstLine(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "_no message_";
        }
        var line = message.Replace("\r\n", "\n").Split('\n')[0].Trim();
        return line.Length == 0 ? "_no message_" : Truncate(line, 200)!;
    }

    private static string Short(string? sha) =>
        string.IsNullOrWhiteSpace(sha) ? "(no sha)" : sha.Trim()[..Math.Min(sha.Trim().Length, 10)];

    private static string? Truncate(string? s, int max) =>
        s is null || s.Length <= max ? s : s[..max];

    /// <summary>What one change accumulated from a single report, before it's written.</summary>
    private sealed class ItemReport(WorkOrderItem item)
    {
        public WorkOrderItem Item { get; } = item;
        public List<(WorkOrderCommitReport Commit, bool Inferred)> Commits { get; } = [];
        public List<WorkOrderPullRequestReport> PullRequests { get; } = [];
        public int LinksAdded { get; set; }
        public bool Blocked { get; set; }

        public IEnumerable<string> LinkUrls()
        {
            foreach (var (commit, _) in Commits)
            {
                if (!string.IsNullOrWhiteSpace(commit.Url))
                {
                    yield return commit.Url;
                }
            }
            foreach (var pr in PullRequests)
            {
                if (!string.IsNullOrWhiteSpace(pr.Url))
                {
                    yield return pr.Url;
                }
            }
        }
    }
}
