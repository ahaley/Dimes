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

        var (actor, _) = await members.ResolveAsync(projectId, actorId, ct);

        var change = new ChangeRequest
        {
            ProjectId = projectId,
            Title = req.Title.Trim(),
            Description = req.Description,
            Kind = req.Kind,
            Priority = req.Priority,
            Status = ChangeStatus.Captured,
            CreatedByActorId = actor.Id,
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
        return change.ToDto();
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
        return change.ToDto();
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

        var sb = new StringBuilder();
        sb.AppendLine($"# Work order — implement In-Development changes ({project.Name})");
        sb.AppendLine();
        sb.AppendLine("## Objective");
        sb.AppendLine();
        sb.AppendLine("You are Claude Code working in this repository. Implement every change request in the");
        sb.AppendLine("\"Changes\" section below against this codebase. This file is the full task list — keep");
        sb.AppendLine("going until each change is implemented and merged, or explicitly recorded as blocked.");
        sb.AppendLine();
        sb.AppendLine("## How to work");
        sb.AppendLine();
        sb.AppendLine("1. **Record the integration branch.** Note the branch you start on (e.g. `main`); call it");
        sb.AppendLine("   the *integration branch*. Every change branches from it and merges back into it.");
        sb.AppendLine("2. **One branch per change**, in order. Create a branch off the *current* integration");
        sb.AppendLine("   branch using the `Branch:` name given for each change below.");
        sb.AppendLine("3. **Implement only that change**, then **verify before committing**: the project must");
        sb.AppendLine("   build and the relevant tests must pass. Never commit a broken change.");
        sb.AppendLine("4. **Commit on the branch.** First line is the change title, then a blank line, then");
        sb.AppendLine("   `Dimes change <id>`. Keep unrelated edits out of the commit.");
        sb.AppendLine("5. **Merge back.** Switch to the integration branch and run");
        sb.AppendLine("   `git merge --no-ff <branch>` so each change is one reviewable merge commit. Resolve");
        sb.AppendLine("   any conflicts and re-verify the build after merging.");
        sb.AppendLine("6. **Sequence matters.** Branch each subsequent change from the *updated* integration");
        sb.AppendLine("   branch so later changes build on earlier ones.");
        sb.AppendLine("7. **If a change can't be completed** (won't build, tests fail, unresolvable conflict),");
        sb.AppendLine("   leave its branch unmerged, check it off as blocked with a one-line reason, and");
        sb.AppendLine("   continue with the remaining changes.");
        sb.AppendLine("8. Work autonomously through the whole list; pause only if a change is too ambiguous to");
        sb.AppendLine("   implement safely. When finished, report what merged and what's blocked.");
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
                sb.AppendLine($"### {n}. {c.Title}{priority}");
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

    public async Task<IReadOnlyList<ChangeRequestDto>> ListAsync(
        Guid projectId, ChangeStatus? status, CancellationToken ct = default)
    {
        var query = db.ChangeRequests.Where(c => c.ProjectId == projectId);
        if (status is not null)
        {
            query = query.Where(c => c.Status == status);
        }

        return await query
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => c.ToDto())
            .ToListAsync(ct);
    }

    public async Task<ChangeRequestDetailDto> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        var change = await db.ChangeRequests
            .Include(c => c.Comments)
            .Include(c => c.ScmLinks)
            .Include(c => c.Evidence)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NotFoundException($"Change request '{id}' not found.");

        return new ChangeRequestDetailDto(
            change.ToDto(),
            change.Comments.OrderBy(c => c.CreatedAt).Select(c => c.ToDto()).ToList(),
            change.Evidence.OrderByDescending(o => o.LastSeen).Select(o => o.ToDto()).ToList(),
            change.ScmLinks.OrderBy(l => l.CreatedAt).Select(l => l.ToDto()).ToList());
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
        return change.ToDto();
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
