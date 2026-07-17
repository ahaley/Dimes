namespace Dimes.Domain.Providers;

/// <summary>Per-call delivery target resolved from a <c>NotificationChannelConfig</c> (+ secret store).
/// Kept separate from the config entity so adapters stay free of persistence concerns — the same shape
/// the LLM/SCM seams use. <see cref="Target"/> is the channel address (for Google Chat, a space resource
/// name like <c>spaces/AAAA</c>); <see cref="Secret"/> is the resolved secret value (for Google Chat, the
/// service-account credentials JSON).</summary>
public sealed record NotificationConnection(string Target, string? Secret);

/// <summary>A rendered notification, ready to send. Kept channel-agnostic: adapters decide how to present
/// a title + body (Google Chat concatenates them into one text message).</summary>
public sealed record NotificationMessage(string Title, string Body);

/// <summary>The thin outbound-notification seam. Concrete adapters (Google Chat now; Webhook/SMTP later)
/// implement this; the drain worker selects one by <see cref="Type"/>. Adapters only deliver — they never
/// read or mutate domain state.</summary>
public interface INotificationChannelProvider
{
    NotificationChannelType Type { get; }

    Task SendAsync(NotificationMessage message, NotificationConnection connection, CancellationToken ct = default);
}
