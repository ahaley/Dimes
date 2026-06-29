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
    DimesDbContext db, LifecycleService lifecycle, MembershipResolver members, IBoardNotifier notifier)
{
    /// <summary>Explicit human submission — enters the lifecycle directly at Captured.</summary>
    public async Task<ChangeRequestDto> CreateAsync(
        Guid projectId, Guid actorId, CreateChangeRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
        {
            throw new BadRequestException("Title is required.");
        }

        var (actor, role) = await members.ResolveAsync(projectId, actorId, ct);

        // An optional recipient at creation: only a Contributor+ may direct work, and the target must be
        // a project member (assignment otherwise has its own endpoint — see AssignAsync).
        if (req.AssigneeActorId is Guid recipientId)
        {
            if (role < MemberRole.Contributor)
            {
                throw new ForbiddenException("Assigning a recipient requires at least the Contributor role.");
            }
            await members.ResolveAsync(projectId, recipientId, ct); // throws ForbiddenException for a non-member
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

        change.Title = req.Title.Trim();
        change.Description = req.Description;
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

        if (req.AssigneeActorId is Guid recipientId)
        {
            await members.ResolveAsync(change.ProjectId, recipientId, ct); // throws ForbiddenException for a non-member
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
    /// change requests: an instruction header plus a numbered, checkboxed section per change.</summary>
    public async Task<MarkdownExport> ExportInDevelopmentAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await db.Projects.FindAsync([projectId], ct)
            ?? throw new NotFoundException($"Project '{projectId}' not found.");

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
                var id8 = c.Id.ToString()[..8];
                var displayKey = project.Key != null && c.Number is int num ? $"{project.Key}-{num} " : string.Empty;
                sb.AppendLine($"### {n}. {displayKey}{c.Title}{priority}");
                sb.AppendLine();
                sb.AppendLine(string.IsNullOrWhiteSpace(c.Description) ? "_No description provided._" : c.Description);
                sb.AppendLine();
                sb.AppendLine($"- Change id: `{c.Id}`");
                sb.AppendLine($"- Branch: `change/{id8}-{Slug(c.Title)}`");
                sb.AppendLine("- [ ] Implemented, verified, committed, merged");
                sb.AppendLine();
                n++;
            }
        }

        // Short UTC timestamp keeps successive downloads from colliding/overwriting in the browser.
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        return new MarkdownExport($"{Slug(project.Name)}-in-development-{stamp}.md", sb.ToString());
    }

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
        return ordered.Select(c => c.ToDto(projectKey)).ToList();
    }

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

        return new ChangeRequestDetailDto(
            change.ToDto(projectKey),
            change.Comments.OrderBy(c => c.CreatedAt).Select(c => c.ToDto()).ToList(),
            change.Evidence.OrderByDescending(o => o.LastSeen).Select(o => o.ToDto()).ToList(),
            change.ScmLinks.OrderBy(l => l.CreatedAt).Select(l => l.ToDto()).ToList(),
            children.Select(c => c.ToDto(projectKey)).ToList());
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
        await db.SaveChangesAsync(ct);
        await notifier.ChangedAsync(change.ProjectId, change.Id, "transitioned", ct);
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
