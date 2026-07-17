namespace Dimes.Domain.Entities;

/// <summary>A per-project outbound notification channel (pass-1: Google Chat). Mirrors
/// <see cref="ObservationSource"/>: a typed channel with a name, a target address, a referenced secret,
/// and an enable flag. The set of events routed here is stored as a JSON array of
/// <see cref="NotificationEventType"/> names. Secrets are referenced, never stored inline.</summary>
public class NotificationChannelConfig : Entity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = default!;

    public NotificationChannelType Type { get; set; }
    public required string Name { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>The channel address. For Google Chat, the space resource name (e.g. <c>spaces/AAAA</c>).</summary>
    public required string Target { get; set; }

    /// <summary>Reference to a secret in the secret store — not the secret itself. For Google Chat, the
    /// name of the service-account credentials JSON.</summary>
    public string? SecretRef { get; set; }

    /// <summary>The subscribed events as a JSON array of <see cref="NotificationEventType"/> names. A JSON
    /// column keeps the set flexible without a join table (the same choice as <c>Observation.Payload</c>).</summary>
    public string EventsJson { get; set; } = "[]";

    // Delivery health, stamped by the drain worker so the settings UI can show a last-delivery badge and
    // surface a dead endpoint instead of failing silently forever.
    public DateTimeOffset? LastDeliveryAt { get; set; }
    public bool? LastDeliveryOk { get; set; }
    public string? LastDeliveryError { get; set; }
}
