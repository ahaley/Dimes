using Dimes.Api.Contracts;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Domain.Lifecycle;
using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Services;

public class ObservationService(DimesDbContext db, LifecycleService lifecycle, MembershipResolver members)
{
    public async Task<ObservationSourceDto> CreateSourceAsync(
        Guid projectId, CreateSourceRequest req, CancellationToken ct = default)
    {
        var project = await db.Projects.FindAsync([projectId], ct)
            ?? throw new NotFoundException($"Project '{projectId}' not found.");

        var source = new ObservationSource
        {
            ProjectId = project.Id,
            Type = req.Type,
            Name = req.Name,
            ConfigJson = req.ConfigJson,
        };
        db.ObservationSources.Add(source);
        await db.SaveChangesAsync(ct);
        return source.ToDto();
    }

    public async Task<IReadOnlyList<ObservationSourceDto>> ListSourcesAsync(Guid projectId, CancellationToken ct = default) =>
        await db.ObservationSources
            .Where(s => s.ProjectId == projectId)
            .OrderBy(s => s.Name)
            .Select(s => s.ToDto())
            .ToListAsync(ct);

    /// <summary>Ingest a captured signal. If a fingerprint is supplied and an open (New/Clustered)
    /// observation already exists for it in the project, aggregate into that one (bump count + last
    /// seen) rather than creating noise. This is the capture-side of the signal→change pipeline.</summary>
    public async Task<ObservationDto> IngestAsync(
        Guid sourceId, IngestObservationRequest req, CancellationToken ct = default)
    {
        var source = await db.ObservationSources.FindAsync([sourceId], ct)
            ?? throw new NotFoundException($"Observation source '{sourceId}' not found.");

        if (string.IsNullOrWhiteSpace(req.Payload))
        {
            throw new BadRequestException("Observation payload is required.");
        }

        if (!string.IsNullOrEmpty(req.Fingerprint))
        {
            var existing = await db.Observations.FirstOrDefaultAsync(
                o => o.ProjectId == source.ProjectId
                     && o.Fingerprint == req.Fingerprint
                     && (o.Status == ObservationStatus.New || o.Status == ObservationStatus.Clustered),
                ct);

            if (existing is not null)
            {
                existing.OccurrenceCount++;
                existing.LastSeen = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
                return existing.ToDto();
            }
        }

        var observation = new Observation
        {
            ProjectId = source.ProjectId,
            SourceId = source.Id,
            Kind = req.Kind,
            Status = ObservationStatus.New,
            Payload = req.Payload,
            ContextMetadata = req.ContextMetadata,
            Fingerprint = req.Fingerprint,
        };
        db.Observations.Add(observation);
        await db.SaveChangesAsync(ct);
        return observation.ToDto();
    }

    public async Task<IReadOnlyList<ObservationDto>> ListInboxAsync(
        Guid projectId, ObservationStatus? status, CancellationToken ct = default)
    {
        var query = db.Observations.Where(o => o.ProjectId == projectId);
        if (status is not null)
        {
            query = query.Where(o => o.Status == status);
        }

        return await query
            .OrderByDescending(o => o.LastSeen)
            .Select(o => o.ToDto())
            .ToListAsync(ct);
    }

    public async Task<ObservationDto> ClusterAsync(Guid observationId, Guid actorId, CancellationToken ct = default)
        => await TransitionAsync(observationId, ObservationStatus.Clustered, actorId, reason: null, ct);

    public async Task<ObservationDto> DismissAsync(
        Guid observationId, Guid actorId, string? reason, CancellationToken ct = default)
        => await TransitionAsync(observationId, ObservationStatus.Dismissed, actorId, reason, ct);

    private async Task<ObservationDto> TransitionAsync(
        Guid observationId, ObservationStatus target, Guid actorId, string? reason, CancellationToken ct)
    {
        var observation = await db.Observations.FindAsync([observationId], ct)
            ?? throw new NotFoundException($"Observation '{observationId}' not found.");

        var (actor, role) = await members.ResolveAsync(observation.ProjectId, actorId, ct);
        var audit = lifecycle.TransitionObservation(observation, target, actor, role, reason);
        db.AuditEvents.Add(audit);
        await db.SaveChangesAsync(ct);
        return observation.ToDto();
    }

    /// <summary>Promote an observation into a new change request, attaching it as evidence and
    /// moving the observation to Promoted. Requires at least Contributor (enforced by the engine).</summary>
    public async Task<ChangeRequestDto> PromoteAsync(
        Guid observationId, Guid actorId, PromoteObservationRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
        {
            throw new BadRequestException("A title is required when promoting an observation.");
        }

        var observation = await db.Observations.FindAsync([observationId], ct)
            ?? throw new NotFoundException($"Observation '{observationId}' not found.");

        var (actor, role) = await members.ResolveAsync(observation.ProjectId, actorId, ct);

        var change = new ChangeRequest
        {
            ProjectId = observation.ProjectId,
            Title = req.Title.Trim(),
            Description = req.Description,
            Kind = ChangeKind.ObservationDriven,
            Status = ChangeStatus.Captured,
            CreatedByActorId = actor.Id,
        };

        // Guarded transition of the observation; also link it as evidence on the new change.
        var obsAudit = lifecycle.TransitionObservation(
            observation, ObservationStatus.Promoted, actor, role, reason: "Promoted to change request");
        observation.ChangeRequest = change;

        db.ChangeRequests.Add(change);
        db.AuditEvents.Add(obsAudit);
        db.AuditEvents.Add(new AuditEvent
        {
            EntityType = AuditEntityType.ChangeRequest,
            EntityId = change.Id,
            ActorId = actor.Id,
            ToStatus = ChangeStatus.Captured.ToString(),
            Action = "CreatedFromObservation",
            Reason = $"Promoted from observation {observation.Id}",
        });

        await db.SaveChangesAsync(ct);
        return change.ToDto();
    }
}
