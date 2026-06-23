using Dimes.Domain.Entities;

namespace Dimes.Domain.Lifecycle;

/// <summary>
/// The single, centralized authority for status changes on ChangeRequests and Observations.
/// Status must only ever move through this service: it validates the transition is structurally
/// legal, enforces the role guard (notably Maintainer-only for the whitelist gate), mutates the
/// entity, and returns the <see cref="AuditEvent"/> the caller must persist alongside it.
/// Pure domain logic — no persistence dependency, so it is trivially unit-testable.
/// </summary>
public class LifecycleService
{
    private const string ChangeTransitionAction = "ChangeTransition";
    private const string ObservationTransitionAction = "ObservationTransition";

    /// <summary>Legal change-request transitions. Any status not listed as a value is unreachable
    /// from that key. Rejected/Duplicate are terminal; Done can only be reopened to InDevelopment.</summary>
    private static readonly IReadOnlyDictionary<ChangeStatus, ChangeStatus[]> ChangeTransitions =
        new Dictionary<ChangeStatus, ChangeStatus[]>
        {
            [ChangeStatus.Captured] = [ChangeStatus.Triaged, ChangeStatus.Approved, ChangeStatus.Rejected, ChangeStatus.Duplicate],
            [ChangeStatus.Triaged] = [ChangeStatus.Approved, ChangeStatus.Rejected, ChangeStatus.Duplicate],
            [ChangeStatus.Approved] = [ChangeStatus.InDevelopment, ChangeStatus.Rejected, ChangeStatus.Duplicate],
            [ChangeStatus.InDevelopment] = [ChangeStatus.InReview, ChangeStatus.Approved, ChangeStatus.Rejected, ChangeStatus.Duplicate],
            [ChangeStatus.InReview] = [ChangeStatus.Done, ChangeStatus.InDevelopment, ChangeStatus.Rejected, ChangeStatus.Duplicate],
            [ChangeStatus.Done] = [ChangeStatus.InDevelopment],
            [ChangeStatus.Rejected] = [],
            [ChangeStatus.Duplicate] = [],
        };

    /// <summary>Minimum role required to move a change INTO a given status. The whitelist gate
    /// (Approved) and acceptance (Done) require Maintainer; the rest require Contributor.</summary>
    private static readonly IReadOnlyDictionary<ChangeStatus, MemberRole> ChangeMinimumRole =
        new Dictionary<ChangeStatus, MemberRole>
        {
            [ChangeStatus.Triaged] = MemberRole.Contributor,
            [ChangeStatus.Approved] = MemberRole.Maintainer,
            [ChangeStatus.InDevelopment] = MemberRole.Contributor,
            [ChangeStatus.InReview] = MemberRole.Contributor,
            [ChangeStatus.Done] = MemberRole.Maintainer,
            [ChangeStatus.Rejected] = MemberRole.Contributor,
            [ChangeStatus.Duplicate] = MemberRole.Contributor,
        };

    /// <summary>Legal observation-inbox transitions. Promoted/Dismissed are terminal.</summary>
    private static readonly IReadOnlyDictionary<ObservationStatus, ObservationStatus[]> ObservationTransitions =
        new Dictionary<ObservationStatus, ObservationStatus[]>
        {
            [ObservationStatus.New] = [ObservationStatus.Clustered, ObservationStatus.Promoted, ObservationStatus.Dismissed],
            [ObservationStatus.Clustered] = [ObservationStatus.Promoted, ObservationStatus.Dismissed],
            [ObservationStatus.Promoted] = [],
            [ObservationStatus.Dismissed] = [],
        };

    /// <summary>Triaging the inbox (cluster/promote/dismiss) requires at least Contributor.</summary>
    private const MemberRole ObservationMinimumRole = MemberRole.Contributor;

    /// <summary>Whether a change-request transition is structurally legal (ignores role).</summary>
    public bool IsChangeTransitionAllowed(ChangeStatus from, ChangeStatus to) =>
        from != to && ChangeTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    /// <summary>The legal next statuses from a given change status (for UI affordances).</summary>
    public IReadOnlyCollection<ChangeStatus> AllowedChangeTransitions(ChangeStatus from) =>
        ChangeTransitions.TryGetValue(from, out var allowed) ? allowed : [];

    /// <summary>
    /// Move <paramref name="change"/> to <paramref name="target"/> on behalf of <paramref name="actor"/>
    /// (acting with <paramref name="actorRole"/> in the change's project). Mutates the entity and
    /// returns the audit event to persist. Throws <see cref="InvalidTransitionException"/> or
    /// <see cref="InsufficientRoleException"/> when guards fail.
    /// </summary>
    public AuditEvent TransitionChange(
        ChangeRequest change,
        ChangeStatus target,
        Actor actor,
        MemberRole actorRole,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentNullException.ThrowIfNull(actor);

        if (!IsChangeTransitionAllowed(change.Status, target))
        {
            throw new InvalidTransitionException(change.Status.ToString(), target.ToString());
        }

        if (ChangeMinimumRole.TryGetValue(target, out var required) && actorRole < required)
        {
            throw new InsufficientRoleException(required, actorRole, target.ToString());
        }

        var from = change.Status;
        change.Status = target;
        change.UpdatedAt = DateTimeOffset.UtcNow;

        // Track acceptance time for the board's recent/older Done split: stamp on entering Done,
        // clear on leaving it (reopen → In Development).
        if (target == ChangeStatus.Done)
        {
            change.CompletedAt = DateTimeOffset.UtcNow;
        }
        else if (from == ChangeStatus.Done)
        {
            change.CompletedAt = null;
        }

        return new AuditEvent
        {
            EntityType = AuditEntityType.ChangeRequest,
            EntityId = change.Id,
            ActorId = actor.Id,
            FromStatus = from.ToString(),
            ToStatus = target.ToString(),
            Action = ChangeTransitionAction,
            Reason = reason,
        };
    }

    /// <summary>Whether an observation-inbox transition is structurally legal.</summary>
    public bool IsObservationTransitionAllowed(ObservationStatus from, ObservationStatus to) =>
        from != to && ObservationTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);

    /// <summary>
    /// Move <paramref name="observation"/> to <paramref name="target"/>. Mutates the entity and
    /// returns the audit event to persist. Promotion to a change request is orchestrated by a
    /// higher-level application service; this only validates and records the inbox status change.
    /// </summary>
    public AuditEvent TransitionObservation(
        Observation observation,
        ObservationStatus target,
        Actor actor,
        MemberRole actorRole,
        string? reason = null)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(actor);

        if (!IsObservationTransitionAllowed(observation.Status, target))
        {
            throw new InvalidTransitionException(observation.Status.ToString(), target.ToString());
        }

        if (actorRole < ObservationMinimumRole)
        {
            throw new InsufficientRoleException(ObservationMinimumRole, actorRole, target.ToString());
        }

        var from = observation.Status;
        observation.Status = target;
        // Note: LastSeen records when the underlying signal was last observed (set on ingest). A
        // moderation transition (cluster/dismiss/promote) is NOT a new sighting, so we must not stamp
        // it here — the transition time is already captured on the AuditEvent below. Overwriting it
        // would corrupt inbox/evidence ordering, which sorts by LastSeen.

        return new AuditEvent
        {
            EntityType = AuditEntityType.Observation,
            EntityId = observation.Id,
            ActorId = actor.Id,
            FromStatus = from.ToString(),
            ToStatus = target.ToString(),
            Action = ObservationTransitionAction,
            Reason = reason,
        };
    }
}
