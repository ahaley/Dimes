using Dimes.Api.Contracts;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Domain.Lifecycle;
using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Services;

public class ChangeRequestService(DimesDbContext db, LifecycleService lifecycle, MembershipResolver members)
{
    /// <summary>Explicit human submission — enters the lifecycle directly at Captured.</summary>
    public async Task<ChangeRequestDto> CreateAsync(
        Guid projectId, CreateChangeRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
        {
            throw new BadRequestException("Title is required.");
        }

        var (actor, _) = await members.ResolveAsync(projectId, req.ActorId, ct);

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
        return change.ToDto();
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
        Guid id, TransitionChangeRequest req, CancellationToken ct = default)
    {
        var change = await db.ChangeRequests.FindAsync([id], ct)
            ?? throw new NotFoundException($"Change request '{id}' not found.");

        var (actor, role) = await members.ResolveAsync(change.ProjectId, req.ActorId, ct);

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
        return change.ToDto();
    }

    public async Task<CommentDto> AddCommentAsync(Guid id, AddCommentRequest req, CancellationToken ct = default)
    {
        var change = await db.ChangeRequests.FindAsync([id], ct)
            ?? throw new NotFoundException($"Change request '{id}' not found.");

        if (string.IsNullOrWhiteSpace(req.Body))
        {
            throw new BadRequestException("Comment body is required.");
        }

        var (actor, _) = await members.ResolveAsync(change.ProjectId, req.ActorId, ct);

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
