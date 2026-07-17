namespace Dimes.Domain.Entities;

/// <summary>An actor's personal opt-out from digest notifications. Absent = opted in (the default). A row
/// with <see cref="ProjectId"/> null is the actor's global preference; a per-project row overrides it for
/// that project. Only human actors are ever notified, so agents never get a row.</summary>
public class NotificationPreference : Entity
{
    public Guid ActorId { get; set; }
    public Actor Actor { get; set; } = default!;

    /// <summary>Null = the actor's global preference; otherwise scoped to one project.</summary>
    public Guid? ProjectId { get; set; }

    /// <summary>When true, exclude this actor from the daily digest (their section is omitted).</summary>
    public bool DigestOptOut { get; set; }
}
