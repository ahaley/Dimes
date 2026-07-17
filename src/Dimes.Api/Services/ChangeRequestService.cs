using System.Text;
using Dimes.Api.Contracts;
using Dimes.Api.Realtime;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Domain.Lifecycle;
using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Services;

public class ChangeRequestService(
    DimesDbContext db, LifecycleService lifecycle, MembershipResolver members, IBoardNotifier notifier,
    INotificationDispatcher notifications)
{
    /// <summary>The human-readable label for a change in a notification: "KEY-N — Title" when the display
    /// key is available, else just the title.</summary>
    private async Task<string> ChangeLabelAsync(ChangeRequest change, CancellationToken ct)
    {
        var key = await ProjectKeyAsync(change.ProjectId, ct);
        var display = key != null && change.Number is int num ? $"{key}-{num}" : null;
        return display != null ? $"{display} — {change.Title}" : change.Title;
    }

    /// <summary>JsonStringEnumConverter accepts raw integers, so an undefined numeric value (e.g.
    /// <c>"kind": 100</c>) deserializes without error and would persist as garbage. Reject it here.</summary>
    private static void EnsureDefined(ChangeKind kind, Priority priority)
    {
        if (!Enum.IsDefined(kind) || !Enum.IsDefined(priority))
        {
            throw new BadRequestException("Unknown kind or priority value.");
        }
    }

    /// <summary>Explicit human submission — enters the lifecycle directly at Captured.</summary>
    public async Task<ChangeRequestDto> CreateAsync(
        Guid projectId, Guid actorId, CreateChangeRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
        {
            throw new BadRequestException("Title is required.");
        }

        EnsureDefined(req.Kind, req.Priority);

        // ObservationDriven is a provenance stamp applied only by promotion (ObservationService.PromoteAsync),
        // where it's kept in lockstep with the evidence link. It can't be chosen on a manual create.
        if (req.Kind == ChangeKind.ObservationDriven)
        {
            throw new BadRequestException(
                "Observation-driven is set only when promoting an observation; it can't be chosen manually.");
        }

        var (actor, role) = await members.ResolveAsync(projectId, actorId, ct);

        // An optional recipient at creation: only a Contributor+ may direct work, and the target must be
        // a project member (assignment otherwise has its own endpoint — see AssignAsync).
        Actor? recipientActor = null;
        if (req.AssigneeActorId is Guid recipientId)
        {
            if (role < MemberRole.Contributor)
            {
                throw new ForbiddenException("Assigning a recipient requires at least the Contributor role.");
            }
            (recipientActor, _) = await members.ResolveAsync(projectId, recipientId, ct); // throws for a non-member
        }

        var change = new ChangeRequest
        {
            ProjectId = projectId,
            Title = req.Title.Trim(),
            Description = req.Description,
            Kind = req.Kind,
            Priority = req.Priority,
            Status = ChangeStatus.Captured,
            CreatedByActorId = actor.Id,
            AssigneeActorId = req.AssigneeActorId,
            Number = await NextNumberAsync(projectId, ct),
        };
        db.ChangeRequests.Add(change);
        db.AuditEvents.Add(new AuditEvent
        {
            EntityType = AuditEntityType.ChangeRequest,
            EntityId = change.Id,
            ActorId = actor.Id,
            ToStatus = ChangeStatus.Captured.ToString(),
            Action = "Created",
        });

        // Notify a recipient assigned at creation (unless they assigned it to themselves).
        if (recipientActor is not null && recipientActor.Id != actor.Id)
        {
            var label = await ChangeLabelAsync(change, ct);
            await notifications.EnqueueAsync(
                projectId, NotificationEventType.AssignedToYou, "Change assigned",
                $"{label} was assigned to {recipientActor.DisplayName} by {actor.DisplayName}.",
                changeId: change.Id, recipientActorId: recipientActor.Id, ct: ct);
        }

        await db.SaveChangesAsync(ct);
        await notifier.ChangedAsync(change.ProjectId, change.Id, "created", ct);
        return change.ToDto(await ProjectKeyAsync(projectId, ct));
    }

    /// <summary>The next per-project change number = current max + 1 (1 for the first). The
    /// (ProjectId, Number) unique index guarantees integrity if two creates ever race.</summary>
    private async Task<int> NextNumberAsync(Guid projectId, CancellationToken ct) =>
        (await db.ChangeRequests.Where(c => c.ProjectId == projectId).MaxAsync(c => (int?)c.Number, ct) ?? 0) + 1;

    private async Task<string?> ProjectKeyAsync(Guid projectId, CancellationToken ct) =>
        await db.Projects.Where(p => p.Id == projectId).Select(p => p.Key).FirstOrDefaultAsync(ct);

    /// <summary>Create many changes atomically (Freestyle Mode's confirm step). Validates the whole batch
    /// up front, then commits all in one transaction so a partial failure can't leave orphaned changes.
    /// Each enters the lifecycle at Captured, exactly like <see cref="CreateAsync"/>.</summary>
    public async Task<IReadOnlyList<ChangeRequestDto>> CreateManyAsync(
        Guid projectId, Guid actorId, IReadOnlyList<CreateChangeRequest> reqs, CancellationToken ct = default)
    {
        if (reqs.Count == 0)
        {
            throw new BadRequestException("At least one change is required.");
        }
        if (reqs.Any(r => string.IsNullOrWhiteSpace(r.Title)))
        {
            throw new BadRequestException("Every change must have a title.");
        }
        foreach (var r in reqs)
        {
            EnsureDefined(r.Kind, r.Priority);
        }
        // ObservationDriven is applied only by promotion; a manual/Freestyle batch can't declare it.
        if (reqs.Any(r => r.Kind == ChangeKind.ObservationDriven))
        {
            throw new BadRequestException(
                "Observation-driven is set only when promoting an observation; it can't be chosen manually.");
        }

        var (actor, _) = await members.ResolveAsync(projectId, actorId, ct);

        var nextNumber = await NextNumberAsync(projectId, ct);
        var changes = new List<ChangeRequest>(reqs.Count);
        foreach (var req in reqs)
        {
            var change = new ChangeRequest
            {
                ProjectId = projectId,
                Title = req.Title.Trim(),
                Description = req.Description,
                Kind = req.Kind,
                Priority = req.Priority,
                Status = ChangeStatus.Captured,
                CreatedByActorId = actor.Id,
                Number = nextNumber++,
            };
            db.ChangeRequests.Add(change);
            db.AuditEvents.Add(new AuditEvent
            {
                EntityType = AuditEntityType.ChangeRequest,
                EntityId = change.Id,
                ActorId = actor.Id,
                ToStatus = ChangeStatus.Captured.ToString(),
                Action = "Created",
            });
            changes.Add(change);
        }

        await db.SaveChangesAsync(ct);
        foreach (var change in changes)
        {
            await notifier.ChangedAsync(change.ProjectId, change.Id, "created", ct);
        }
        var projectKey = await ProjectKeyAsync(projectId, ct);
        return changes.Select(c => c.ToDto(projectKey)).ToList();
    }

    /// <summary>Edit a change's title/description/priority after creation. Permitted to the original
    /// author or a Maintainer; recorded as a DetailsEdited audit event.</summary>
    public async Task<ChangeRequestDto> UpdateDetailsAsync(
        Guid id, Guid actorId, UpdateChangeDetailsRequest req, CancellationToken ct = default)
    {
        var change = await db.ChangeRequests.FindAsync([id], ct)
            ?? throw new NotFoundException($"Change request '{id}' not found.");

        var (actor, role) = await members.ResolveAsync(change.ProjectId, actorId, ct);
        if (actor.Id != change.CreatedByActorId && role != MemberRole.Maintainer)
        {
            throw new ForbiddenException("Only the author or a Maintainer can edit this change.");
        }

        if (string.IsNullOrWhiteSpace(req.Title))
        {
            throw new BadRequestException("Title is required.");
        }

        EnsureDefined(req.Kind, req.Priority);

        // Kind is editable post-create, but changing it can't be allowed to break Epic composition:
        // an Epic that still composes children can't stop being an Epic, and a change already composed
        // inside an Epic can't itself become one (an Epic can't nest in an Epic — see AddChildAsync).
        if (req.Kind != change.Kind)
        {
            // ObservationDriven is a provenance stamp set when a change is promoted from an observation
            // (see ObservationService.PromoteAsync); it must stay in lockstep with the evidence link, so
            // it can neither be added to nor stripped from a change after creation.
            if (req.Kind == ChangeKind.ObservationDriven || change.Kind == ChangeKind.ObservationDriven)
            {
                throw new BadRequestException(
                    "A change's observation-driven provenance can't be changed after creation.");
            }
            if (change.Kind == ChangeKind.Epic
                && await db.ChangeRequests.AnyAsync(c => c.ParentChangeRequestId == change.Id, ct))
            {
                throw new BadRequestException(
                    "This Epic composes child change requests; remove them before changing its kind.");
            }
            if (req.Kind == ChangeKind.Epic && change.ParentChangeRequestId is not null)
            {
                throw new BadRequestException(
                    "A change composed in an Epic cannot itself become an Epic; remove it from its Epic first.");
            }
        }

        change.Title = req.Title.Trim();
        change.Description = req.Description;
        change.Kind = req.Kind;
        change.Priority = req.Priority;
        change.UpdatedAt = DateTimeOffset.UtcNow;

        db.AuditEvents.Add(new AuditEvent
        {
            EntityType = AuditEntityType.ChangeRequest,
            EntityId = change.Id,
            ActorId = actor.Id,
            Action = "DetailsEdited",
        });
        await db.SaveChangesAsync(ct);
        await notifier.ChangedAsync(change.ProjectId, change.Id, "updated", ct);
        return change.ToDto(await ProjectKeyAsync(change.ProjectId, ct));
    }

    /// <summary>Set or clear a change's recipient. Requires Contributor+ (anyone working the project can
    /// direct/claim work); a non-null recipient must be a project member. Records an "Assigned" audit
    /// event.</summary>
    public async Task<ChangeRequestDto> AssignAsync(
        Guid id, Guid actorId, AssignChangeRequest req, CancellationToken ct = default)
    {
        var change = await db.ChangeRequests.FindAsync([id], ct)
            ?? throw new NotFoundException($"Change request '{id}' not found.");

        var (actor, role) = await members.ResolveAsync(change.ProjectId, actorId, ct);
        if (role < MemberRole.Contributor)
        {
            throw new ForbiddenException("Assigning a recipient requires at least the Contributor role.");
        }

        Actor? recipientActor = null;
        if (req.AssigneeActorId is Guid recipientId)
        {
            (recipientActor, _) = await members.ResolveAsync(change.ProjectId, recipientId, ct); // throws for a non-member
        }

        change.AssigneeActorId = req.AssigneeActorId;
        change.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(new AuditEvent
        {
            EntityType = AuditEntityType.ChangeRequest,
            EntityId = change.Id,
            ActorId = actor.Id,
            Action = "Assigned",
        });

        // Notify the new recipient (unless they assigned it to themselves, or it was just cleared).
        if (recipientActor is not null && recipientActor.Id != actor.Id)
        {
            var label = await ChangeLabelAsync(change, ct);
            await notifications.EnqueueAsync(
                change.ProjectId, NotificationEventType.AssignedToYou, "Change assigned",
                $"{label} was assigned to {recipientActor.DisplayName} by {actor.DisplayName}.",
                changeId: change.Id, recipientActorId: recipientActor.Id, ct: ct);
        }

        await db.SaveChangesAsync(ct);
        await notifier.ChangedAsync(change.ProjectId, change.Id, "updated", ct);
        return change.ToDto(await ProjectKeyAsync(change.ProjectId, ct));
    }

    /// <summary>Compose an existing change into an Epic: set its parent to <paramref name="epicId"/>.
    /// Requires Contributor+ (composition is a working-the-project edit, like assignment). The parent must
    /// be an Epic and the child a same-project, non-Epic change that isn't already composed elsewhere
    /// (re-parent by removing first). Records an "AddedToEpic" audit event on the child.</summary>
    public async Task<ChangeRequestDto> AddChildAsync(
        Guid epicId, Guid actorId, Guid childId, CancellationToken ct = default)
    {
        var epic = await db.ChangeRequests.FindAsync([epicId], ct)
            ?? throw new NotFoundException($"Change request '{epicId}' not found.");

        var (actor, role) = await members.ResolveAsync(epic.ProjectId, actorId, ct);
        if (role < MemberRole.Contributor)
        {
            throw new ForbiddenException("Composing an Epic requires at least the Contributor role.");
        }

        if (epic.Kind != ChangeKind.Epic)
        {
            throw new BadRequestException("Only an Epic can compose child change requests.");
        }

        if (childId == epicId)
        {
            throw new BadRequestException("A change cannot be composed into itself.");
        }

        var child = await db.ChangeRequests.FindAsync([childId], ct)
            ?? throw new NotFoundException($"Change request '{childId}' not found.");

        if (child.ProjectId != epic.ProjectId)
        {
            throw new BadRequestException("A child must belong to the same project as the Epic.");
        }
        if (child.Kind == ChangeKind.Epic)
        {
            throw new BadRequestException("An Epic cannot be composed into another Epic.");
        }
        if (child.ParentChangeRequestId is Guid existing)
        {
            throw new BadRequestException(existing == epicId
                ? "This change is already composed in this Epic."
                : "This change is already composed in another Epic; remove it from that one first.");
        }

        child.ParentChangeRequestId = epicId;
        child.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(new AuditEvent
        {
            EntityType = AuditEntityType.ChangeRequest,
            EntityId = child.Id,
            ActorId = actor.Id,
            Action = "AddedToEpic",
        });
        await db.SaveChangesAsync(ct);
        await notifier.ChangedAsync(child.ProjectId, child.Id, "updated", ct);
        return child.ToDto(await ProjectKeyAsync(child.ProjectId, ct));
    }

    /// <summary>Break a composed change out of its Epic: clear its parent. Requires Contributor+; the
    /// child must currently be composed in <paramref name="epicId"/>. Records a "RemovedFromEpic" audit
    /// event on the child.</summary>
    public async Task<ChangeRequestDto> RemoveChildAsync(
        Guid epicId, Guid actorId, Guid childId, CancellationToken ct = default)
    {
        var child = await db.ChangeRequests.FindAsync([childId], ct)
            ?? throw new NotFoundException($"Change request '{childId}' not found.");

        var (actor, role) = await members.ResolveAsync(child.ProjectId, actorId, ct);
        if (role < MemberRole.Contributor)
        {
            throw new ForbiddenException("Composing an Epic requires at least the Contributor role.");
        }

        if (child.ParentChangeRequestId != epicId)
        {
            throw new BadRequestException("This change is not composed in that Epic.");
        }

        child.ParentChangeRequestId = null;
        child.UpdatedAt = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(new AuditEvent
        {
            EntityType = AuditEntityType.ChangeRequest,
            EntityId = child.Id,
            ActorId = actor.Id,
            Action = "RemovedFromEpic",
        });
        await db.SaveChangesAsync(ct);
        await notifier.ChangedAsync(child.ProjectId, child.Id, "updated", ct);
        return child.ToDto(await ProjectKeyAsync(child.ProjectId, ct));
    }

    /// <summary>Generate a single Claude Code "work order" markdown for a project's In-Development
    /// change requests: an instruction header plus a numbered, checkboxed section per change. Records the
    /// export as a <see cref="WorkOrder"/> and embeds a capability token so the executing agent can report
    /// its results back (see <c>WorkOrderService.ReportResultsAsync</c>) — which is why this mints a
    /// credential and must not be reachable by a GET.
    /// <paramref name="apiBaseUrl"/> is the caller-facing origin (e.g. <c>https://dimes.example</c>) used to
    /// build the report-back URL; passed in rather than resolved here so the service stays HTTP-free.</summary>
    public async Task<MarkdownExport> ExportInDevelopmentAsync(
        Guid projectId, Guid actorId, string? apiBaseUrl, CancellationToken ct = default)
    {
        var project = await db.Projects.FindAsync([projectId], ct)
            ?? throw new NotFoundException($"Project '{projectId}' not found.");

        // The exported token carries this actor's authority, so establish it here — and re-check it on
        // every report, which is what makes removing a member revoke their outstanding work orders.
        var (actor, _) = await members.ResolveAsync(projectId, actorId, ct);

        // Sort in memory: Priority is persisted as a string, so a DB ORDER BY would sort it
        // alphabetically (e.g. "None" before "High") rather than by severity.
        var changes = (await db.ChangeRequests
            .Where(c => c.ProjectId == projectId && c.Status == ChangeStatus.InDevelopment)
            .ToListAsync(ct))
            .OrderByDescending(c => c.Priority)
            .ThenBy(c => c.Title)
            .ToList();

        // Use the project's editable guidance if present; otherwise fall back to the built-in default
        // (covers projects created after the one-time seed, or whose row was reset).
        var instruction = await db.SystemInstructions
            .Where(s => s.ProjectId == projectId && s.Kind == SystemInstructionKind.ExportWorkOrder)
            .Select(s => s.Content)
            .FirstOrDefaultAsync(ct);
        var guidance = string.IsNullOrWhiteSpace(instruction) ? SystemInstructionDefaults.ExportWorkOrder : instruction;

        // Short UTC timestamp keeps successive downloads from colliding/overwriting in the browser.
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var fileName = $"{Slug(project.Name)}-in-development-{stamp}.md";

        // Nothing to report on means the token would be pure liability, so mint one only when there's work.
        WorkOrder? workOrder = null;
        string? token = null;
        if (changes.Count > 0)
        {
            token = WorkOrderToken.Mint();
            workOrder = new WorkOrder
            {
                ProjectId = projectId,
                ExportedByActorId = actor.Id,
                FileName = fileName,
                TokenHash = WorkOrderToken.Hash(token),
            };
            foreach (var c in changes)
            {
                workOrder.Items.Add(new WorkOrderItem
                {
                    ChangeRequestId = c.Id,
                    TitleSnapshot = c.Title,
                    BranchName = BranchName(c),
                });
            }
            db.WorkOrders.Add(workOrder);
            await db.SaveChangesAsync(ct);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# Work order — implement In-Development changes ({project.Name})");
        sb.AppendLine();
        AppendBlock(sb, guidance);
        sb.AppendLine();
        sb.AppendLine("## Changes");
        sb.AppendLine();

        if (changes.Count == 0)
        {
            sb.AppendLine("_No change requests are currently In Development._");
        }
        else
        {
            var n = 1;
            foreach (var c in changes)
            {
                var priority = c.Priority == Priority.None ? string.Empty : $" (priority: {c.Priority})";
                var displayKey = project.Key != null && c.Number is int num ? $"{project.Key}-{num} " : string.Empty;
                sb.AppendLine($"### {n}. {displayKey}{c.Title}{priority}");
                sb.AppendLine();
                sb.AppendLine(string.IsNullOrWhiteSpace(c.Description) ? "_No description provided._" : c.Description);
                sb.AppendLine();
                sb.AppendLine($"- Change id: `{c.Id}`");
                sb.AppendLine($"- Branch: `{BranchName(c)}`");
                sb.AppendLine("- [ ] Implemented, verified, committed, merged");
                sb.AppendLine();
                n++;
            }
        }

        if (workOrder is not null && token is not null)
        {
            AppendReportBack(sb, workOrder, token, changes[0], apiBaseUrl);
        }

        return new MarkdownExport(fileName, sb.ToString());
    }

    /// <summary>The change spine in lifecycle order. Single-sourced because it is both the CSV's filter and
    /// its primary sort, and the two must not drift. Rejected/Duplicate are deliberately absent: they are
    /// exits from the funnel rather than stages in it, which is why the board also keeps them out of the
    /// columns and in a separate Closed strip.</summary>
    private static readonly ChangeStatus[] CsvSpine =
    [
        ChangeStatus.Captured, ChangeStatus.Triaged, ChangeStatus.Approved,
        ChangeStatus.InDevelopment, ChangeStatus.InReview, ChangeStatus.Done,
    ];

    /// <summary>Human labels for the CSV's enum columns. The wire format is camel-case (it mirrors the C#
    /// enum), but a spreadsheet is read by people, so these use the spec's own wording.</summary>
    private static string StatusLabel(ChangeStatus s) => s switch
    {
        ChangeStatus.InDevelopment => "In Development",
        ChangeStatus.InReview => "In Review",
        _ => s.ToString(),
    };

    private static string KindLabel(ChangeKind k) => k switch
    {
        ChangeKind.ObservationDriven => "Observation-driven",
        _ => k.ToString(),
    };

    /// <summary>Export a project's change list as a CSV snapshot, ordered along the lifecycle spine
    /// (Captured first, Done last) and by change number within each status.
    ///
    /// Deliberately unlike <see cref="ExportInDevelopmentAsync"/>: it mints nothing, records nothing and
    /// spans the whole board, so it needs no actor authority beyond the caller's project read access and is
    /// safe to reach by GET.</summary>
    public async Task<CsvExport> ExportChangesCsvAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await db.Projects.FindAsync([projectId], ct)
            ?? throw new NotFoundException($"Project '{projectId}' not found.");

        // Sort in memory: Status is persisted as a string, so a DB ORDER BY would sort it alphabetically
        // (Approved, Captured, Done, …) rather than along the lifecycle — the same trap the work-order
        // export sidesteps for Priority.
        var changes = (await db.ChangeRequests
            .Where(c => c.ProjectId == projectId && CsvSpine.Contains(c.Status))
            .Include(c => c.Assignee)
            .ToListAsync(ct))
            .OrderBy(c => Array.IndexOf(CsvSpine, c.Status))
            // Number is nullable only until the startup backfill reaches a row; an un-numbered change sorts
            // last rather than ahead of DIMES-1.
            .ThenBy(c => c.Number ?? int.MaxValue)
            .ToList();

        var sb = new StringBuilder();
        Csv.AppendRow(sb, "Key", "Title", "Status", "Kind", "Priority", "Recipient", "CreatedAt", "CompletedAt");

        foreach (var c in changes)
        {
            // DisplayKey is derived at DTO-mapping time and isn't on the entity, so build it the same way
            // here. Blank when either half is missing — a bare number would read as a key and mislead.
            var key = project.Key != null && c.Number is int num ? $"{project.Key}-{num}" : string.Empty;

            Csv.AppendRow(
                sb,
                key,
                c.Title,
                StatusLabel(c.Status),
                KindLabel(c.Kind),
                c.Priority.ToString(),
                // "Unassigned" is the word the create modal and detail view already use for an unset
                // recipient, rather than a blank cell that reads as missing data.
                c.Assignee?.DisplayName ?? "Unassigned",
                c.CreatedAt.ToUniversalTime().ToString("u"),
                c.CompletedAt?.ToUniversalTime().ToString("u") ?? string.Empty);
        }

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        return new CsvExport($"{Slug(project.Name)}-changes-{stamp}.csv", sb.ToString());
    }

    /// <summary>The deterministic branch name for a change. Both the rendered `Branch:` line and the stored
    /// <see cref="WorkOrderItem.BranchName"/> come from here, so a report that names a branch matches by
    /// exact comparison against a string Dimes itself minted — they cannot drift apart.</summary>
    private static string BranchName(ChangeRequest c) => $"change/{c.Id.ToString()[..8]}-{Slug(c.Title)}";

    /// <summary>The report-back contract, appended to every non-empty export. This lives in the renderer
    /// rather than the editable <see cref="SystemInstructionDefaults.ExportWorkOrder"/> guidance for three
    /// reasons: it carries a live per-export secret (the instruction is static text with no templating), it
    /// is generated per-export in the same way the `Change id:`/`Branch:` lines are generated per-change,
    /// and — decisively — <c>SystemInstructionBootstrapper</c> seeds each project its own copy of the
    /// default, so editing the default would never reach a single existing project.</summary>
    private static void AppendReportBack(
        StringBuilder sb, WorkOrder workOrder, string token, ChangeRequest sample, string? apiBaseUrl)
    {
        var path = $"/api/work-orders/{token}/results";
        var url = string.IsNullOrWhiteSpace(apiBaseUrl) ? path : $"{apiBaseUrl.TrimEnd('/')}{path}";

        sb.AppendLine("## Report back");
        sb.AppendLine();
        sb.AppendLine("When you're done — or blocked — POST your results to Dimes. It attaches your commits");
        sb.AppendLine("and PRs to each change and prompts a human to move it to In Review. Reporting never");
        sb.AppendLine("changes a change's status by itself, so nothing is lost by reporting partial work.");
        sb.AppendLine();
        sb.AppendLine("> **This URL contains a credential unique to this export. Do not commit this file.**");
        sb.AppendLine($"> It stops working on {workOrder.ExpiresAt:yyyy-MM-dd}.");
        sb.AppendLine();
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            sb.AppendLine("_Prefix the path below with your Dimes origin (e.g. `https://dimes.example`)._");
            sb.AppendLine();
        }
        sb.AppendLine("```bash");
        sb.AppendLine($"curl -X POST {url} \\");
        sb.AppendLine("  -H 'Content-Type: application/json' -d '{");
        sb.AppendLine("  \"summary\": \"Integrated all but one change.\",");
        sb.AppendLine("  \"commits\": [");
        sb.AppendLine("    {");
        sb.AppendLine("      \"sha\": \"a1b2c3d\",");
        sb.AppendLine($"      \"message\": \"{EscapeJson(sample.Title)}\\n\\nDimes change {sample.Id}\",");
        sb.AppendLine($"      \"branch\": \"{BranchName(sample)}\",");
        sb.AppendLine("      \"url\": \"https://github.com/owner/repo/commit/a1b2c3d\"");
        sb.AppendLine("    }");
        sb.AppendLine("  ],");
        sb.AppendLine("  \"pullRequests\": [{ \"url\": \"https://github.com/owner/repo/pull/1\" }],");
        sb.AppendLine($"  \"blocked\": [{{ \"changeId\": \"{sample.Id}\", \"reason\": \"Needs a product decision.\" }}]");
        sb.AppendLine("}'");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("- Every field except `sha` and `message` is optional; send only what you have.");
        sb.AppendLine("- A commit is matched by its `Dimes change <id>` trailer. Without one, its `branch`");
        sb.AppendLine("  is used as a fallback — so keep the branch names given above.");
        sb.AppendLine("- A commit `url` becomes a link on the change; a bare `sha` is recorded in the comment.");
        sb.AppendLine("- Report anything you couldn't finish in `blocked` — that's how a human learns it needs");
        sb.AppendLine("  them, and it's better than silence.");
        sb.AppendLine("- Only the changes listed above are accepted; anything else comes back in `ignored`.");
        sb.AppendLine("- Reporting twice is safe: re-sending the same results changes nothing.");
        sb.AppendLine();
    }

    /// <summary>Minimal JSON string escaping for the sample payload embedded in the curl recipe.</summary>
    private static string EscapeJson(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Slug(string name)
    {
        var slug = new string(name.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return string.IsNullOrEmpty(slug) ? "project" : slug;
    }

    /// <summary>Append a multi-line block one line at a time so every line ends with the platform newline,
    /// regardless of how the source text stores its breaks (a stored instruction may carry \n or \r\n). This
    /// reproduces the historical line-by-line <see cref="StringBuilder.AppendLine(string)"/> output exactly.</summary>
    private static void AppendBlock(StringBuilder sb, string text)
    {
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            sb.AppendLine(line);
        }
    }

    /// <summary>Read authority for a change by id: a member of its project, or a site admin. Used by the
    /// id-routed GET endpoints (detail, audit) which carry no projectId in the route. Throws
    /// <see cref="ForbiddenException"/> for non-members and <see cref="NotFoundException"/> if absent.</summary>
    public async Task EnsureCanReadChangeAsync(
        Guid changeId, Guid callerActorId, bool callerIsSiteAdmin, CancellationToken ct = default)
    {
        if (callerIsSiteAdmin)
        {
            return;
        }

        var projectId = await db.ChangeRequests
            .Where(c => c.Id == changeId)
            .Select(c => (Guid?)c.ProjectId)
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException($"Change request '{changeId}' not found.");
        await members.ResolveAsync(projectId, callerActorId, ct); // throws ForbiddenException for non-members
    }

    public async Task<IReadOnlyList<ChangeRequestDto>> ListAsync(
        Guid projectId, ChangeStatus? status, CancellationToken ct = default)
    {
        var projectKey = await ProjectKeyAsync(projectId, ct);
        var query = db.ChangeRequests.Where(c => c.ProjectId == projectId);
        if (status is not null)
        {
            query = query.Where(c => c.Status == status);
        }

        var ordered = await query
            // Manual board order first (0 = unordered → newest-updated-first within those).
            .OrderBy(c => c.SortOrder)
            .ThenByDescending(c => c.UpdatedAt)
            .ToListAsync(ct);

        var reports = await LatestReportsAsync(projectId, ct);
        return ordered.Select(c => c.ToDto(projectKey, Report(reports, c.Id))).ToList();
    }

    /// <summary>Each change's most recent work-order report, for the board card's affordance. Resolved in
    /// memory on purpose: a re-exported change has an item in every order it appeared in, and the
    /// group-then-take-latest that picks between them is exactly the shape SQLite's provider can't
    /// translate (compare the in-memory Priority sort above, for the same class of reason).</summary>
    private async Task<Dictionary<Guid, WorkOrderItem>> LatestReportsAsync(
        Guid projectId, CancellationToken ct)
    {
        var items = await db.WorkOrderItems
            .Where(i => i.WorkOrder.ProjectId == projectId && i.Status != WorkOrderItemStatus.Pending)
            .ToListAsync(ct);
        return items
            .GroupBy(i => i.ChangeRequestId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.ReportedAt).First());
    }

    private static WorkOrderItem? Report(Dictionary<Guid, WorkOrderItem> reports, Guid changeId) =>
        reports.TryGetValue(changeId, out var item) ? item : null;

    /// <summary>Per-project counts of the caller's open (non-terminal) assigned change requests, for the
    /// sidebar "assigned to you" indicator. Assignees are always project members, so this is inherently
    /// scoped to the caller's projects. Terminal statuses (Done/Rejected/Duplicate) are excluded; only
    /// projects with at least one match are returned.</summary>
    public async Task<IReadOnlyList<ProjectAssignmentCountDto>> AssignedOpenCountsAsync(
        Guid actorId, CancellationToken ct = default)
    {
        return await db.ChangeRequests
            .Where(c => c.AssigneeActorId == actorId
                && c.Status != ChangeStatus.Done
                && c.Status != ChangeStatus.Rejected
                && c.Status != ChangeStatus.Duplicate)
            .GroupBy(c => c.ProjectId)
            .Select(g => new ProjectAssignmentCountDto(g.Key, g.Count()))
            .ToListAsync(ct);
    }

    /// <summary>Persist a manual within-column order from board drag-and-drop. Assigns SortOrder 1..n to
    /// the given changes in order; all ids must belong to the project and the named status. Not a
    /// lifecycle/details change, so no audit event is written.</summary>
    public async Task ReorderAsync(
        Guid projectId, Guid actorId, ReorderChangesRequest req, CancellationToken ct = default)
    {
        var (_, role) = await members.ResolveAsync(projectId, actorId, ct);
        if (role < MemberRole.Contributor)
        {
            throw new ForbiddenException("Reordering the board requires at least the Contributor role.");
        }

        var inColumn = await db.ChangeRequests
            .Where(c => c.ProjectId == projectId && c.Status == req.Status)
            .ToListAsync(ct);
        var byId = inColumn.ToDictionary(c => c.Id);

        if (req.OrderedIds.Count != inColumn.Count || req.OrderedIds.Any(id => !byId.ContainsKey(id)))
        {
            throw new BadRequestException("The ordered ids must be exactly the changes in that column.");
        }

        for (var i = 0; i < req.OrderedIds.Count; i++)
        {
            byId[req.OrderedIds[i]].SortOrder = i + 1;
        }
        await db.SaveChangesAsync(ct);
        await notifier.ChangedAsync(projectId, Guid.Empty, "reordered", ct);
    }

    public async Task<ChangeRequestDetailDto> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        var change = await db.ChangeRequests
            .Include(c => c.Comments)
            .Include(c => c.ScmLinks)
            .Include(c => c.Evidence)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException($"Change request '{id}' not found.");

        var projectKey = await ProjectKeyAsync(change.ProjectId, ct);

        // Composed children (only meaningful for an Epic, but harmless to query for any change).
        var children = await db.ChangeRequests
            .Where(c => c.ParentChangeRequestId == id)
            .OrderBy(c => c.Number)
            .ToListAsync(ct);

        var reports = await LatestReportsAsync(change.ProjectId, ct);

        return new ChangeRequestDetailDto(
            change.ToDto(projectKey, Report(reports, change.Id)),
            change.Comments.OrderBy(c => c.CreatedAt).Select(c => c.ToDto()).ToList(),
            change.Evidence.OrderByDescending(o => o.LastSeen).Select(o => o.ToDto()).ToList(),
            change.ScmLinks.OrderBy(l => l.CreatedAt).Select(l => l.ToDto()).ToList(),
            children.Select(c => c.ToDto(projectKey, Report(reports, c.Id))).ToList());
    }

    public async Task<ChangeRequestDto> TransitionAsync(
        Guid id, Guid actorId, TransitionChangeRequest req, CancellationToken ct = default)
    {
        var change = await db.ChangeRequests.FindAsync([id], ct)
            ?? throw new NotFoundException($"Change request '{id}' not found.");

        var (actor, role) = await members.ResolveAsync(change.ProjectId, actorId, ct);

        if (req.Target == ChangeStatus.Duplicate)
        {
            if (req.DuplicateOfId is null)
            {
                throw new BadRequestException("DuplicateOfId is required when marking a change as Duplicate.");
            }

            var exists = await db.ChangeRequests.AnyAsync(c => c.Id == req.DuplicateOfId, ct);
            if (!exists)
            {
                throw new NotFoundException($"Duplicate target '{req.DuplicateOfId}' not found.");
            }

            change.DuplicateOfId = req.DuplicateOfId;
        }

        var audit = lifecycle.TransitionChange(change, req.Target, actor, role, req.Reason);
        db.AuditEvents.Add(audit);

        // Epic composition: a child must always match its Epic's state exactly, so cascade this transition
        // to every composed child — forcing the same target regardless of step-by-step legality (the role
        // guard above already authorized the move). Children inherit the Epic's Duplicate target too.
        var cascaded = new List<Guid>();
        if (change.Kind == ChangeKind.Epic)
        {
            var children = await db.ChangeRequests.Where(c => c.ParentChangeRequestId == change.Id).ToListAsync(ct);
            foreach (var child in children)
            {
                // SyncChildStatus returns null (and mutates nothing) when the child is already at the
                // target, so only touch the child — including its inherited Duplicate target — when it moves.
                var childAudit = lifecycle.SyncChildStatus(child, req.Target, actor);
                if (childAudit is not null)
                {
                    child.DuplicateOfId = req.Target == ChangeStatus.Duplicate ? req.DuplicateOfId : null;
                    db.AuditEvents.Add(childAudit);
                    cascaded.Add(child.Id);
                }
            }
        }

        // Close the work-order loop. Entering In Review is the human confirmation of an agent's reported
        // work, so settle any outstanding claim on this change (and on anything the Epic cascade moved with
        // it). This is bookkeeping on an already-authorized transition — the status itself moved through
        // LifecycleService above, which stays the only door into the state machine.
        if (req.Target == ChangeStatus.InReview)
        {
            var settling = new List<Guid>(cascaded) { change.Id };
            var claimed = await db.WorkOrderItems
                .Where(i => settling.Contains(i.ChangeRequestId) && i.Status == WorkOrderItemStatus.Reported)
                .ToListAsync(ct);
            foreach (var item in claimed)
            {
                item.Status = WorkOrderItemStatus.Confirmed;
            }
        }

        // Entering Triaged is the moment a change starts waiting on the Maintainer approval gate — the
        // product's single most important latency. Nudge the project's channels (the body names the change;
        // the shared space reaches whoever holds the gate).
        if (req.Target == ChangeStatus.Triaged)
        {
            var label = await ChangeLabelAsync(change, ct);
            await notifications.EnqueueAsync(
                change.ProjectId, NotificationEventType.AwaitingApproval, "Change awaiting approval",
                $"{label} is now Triaged and awaiting a Maintainer's approval.",
                changeId: change.Id, ct: ct);
        }

        await db.SaveChangesAsync(ct);
        await notifier.ChangedAsync(change.ProjectId, change.Id, "transitioned", ct);
        foreach (var childId in cascaded)
        {
            await notifier.ChangedAsync(change.ProjectId, childId, "transitioned", ct);
        }
        return change.ToDto(await ProjectKeyAsync(change.ProjectId, ct));
    }

    public async Task<CommentDto> AddCommentAsync(Guid id, Guid actorId, AddCommentRequest req, CancellationToken ct = default)
    {
        var change = await db.ChangeRequests.FindAsync([id], ct)
            ?? throw new NotFoundException($"Change request '{id}' not found.");

        if (string.IsNullOrWhiteSpace(req.Body))
        {
            throw new BadRequestException("Comment body is required.");
        }

        var (actor, _) = await members.ResolveAsync(change.ProjectId, actorId, ct);

        var comment = new Comment
        {
            ChangeRequestId = change.Id,
            AuthorActorId = actor.Id,
            Body = req.Body,
            Kind = CommentKind.Human,
        };
        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);
        return comment.ToDto();
    }

    public async Task<IReadOnlyList<AuditEventDto>> GetAuditAsync(Guid id, CancellationToken ct = default)
    {
        var exists = await db.ChangeRequests.AnyAsync(c => c.Id == id, ct);
        if (!exists)
        {
            throw new NotFoundException($"Change request '{id}' not found.");
        }

        return await db.AuditEvents
            .Where(e => e.EntityType == AuditEntityType.ChangeRequest && e.EntityId == id)
            .OrderBy(e => e.Timestamp)
            .Select(e => e.ToDto())
            .ToListAsync(ct);
    }
}
