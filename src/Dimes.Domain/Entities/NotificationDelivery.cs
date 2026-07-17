namespace Dimes.Domain.Entities;

/// <summary>The outbox row for one pending/attempted delivery. Written in the same transaction as the
/// triggering audit event (so a delivery can never exist for an event that didn't commit), then drained
/// asynchronously by the background worker with retry/backoff. A projection of the audit log — it never
/// drives a lifecycle transition.</summary>
public class NotificationDelivery : Entity
{
    public Guid ProjectId { get; set; }

    /// <summary>The channel this delivery targets. Restrict, like the actor FKs: deleting a channel with
    /// outstanding deliveries is blocked (the service clears them first), keeping the outbox consistent.</summary>
    public Guid ChannelConfigId { get; set; }
    public NotificationChannelConfig ChannelConfig { get; set; } = default!;

    public NotificationEventType Event { get; set; }

    // The message is rendered at enqueue time (the event context is richest there) and stored, so the
    // worker is a pure sender that needs no domain lookups to deliver.
    public required string Title { get; set; }
    public required string Body { get; set; }

    public NotificationDeliveryStatus Status { get; set; } = NotificationDeliveryStatus.Pending;
    public int Attempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? LastError { get; set; }

    /// <summary>Optional context for diagnostics/filtering (not FKs to keep the outbox decoupled and
    /// purgeable): the change the event is about, and the human the delivery concerns.</summary>
    public Guid? ChangeRequestId { get; set; }
    public Guid? RecipientActorId { get; set; }
}
