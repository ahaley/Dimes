using System.Text.Json;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Services;

/// <summary>Stages outbound notification deliveries for an event. Injected into the services that own the
/// event's context (change lifecycle, work orders); each calls <see cref="EnqueueAsync"/> right before its
/// existing <c>SaveChangesAsync</c>, so the outbox rows commit in the SAME transaction as the audit event —
/// a delivery can never exist for an event that didn't happen. Enqueue only stages rows; the
/// <see cref="NotificationDrainService"/> delivers them asynchronously with retry. This is a projection of
/// the audit log and never touches lifecycle state.</summary>
public interface INotificationDispatcher
{
    Task EnqueueAsync(
        Guid projectId,
        NotificationEventType eventType,
        string title,
        string body,
        Guid? changeId = null,
        Guid? recipientActorId = null,
        CancellationToken ct = default);
}

public sealed class NotificationDispatcher(DimesDbContext db) : INotificationDispatcher
{
    public async Task EnqueueAsync(
        Guid projectId,
        NotificationEventType eventType,
        string title,
        string body,
        Guid? changeId = null,
        Guid? recipientActorId = null,
        CancellationToken ct = default)
    {
        // Only enabled channels that subscribe to this event. A shared destination (a Google Chat space)
        // gets one delivery per channel, not per recipient — the body names whoever it concerns.
        var channels = await db.NotificationChannelConfigs
            .Where(c => c.ProjectId == projectId && c.Enabled)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var channel in channels)
        {
            if (!Subscribes(channel, eventType))
            {
                continue;
            }

            db.NotificationDeliveries.Add(new NotificationDelivery
            {
                ProjectId = projectId,
                ChannelConfigId = channel.Id,
                Event = eventType,
                Title = title,
                Body = body,
                Status = NotificationDeliveryStatus.Pending,
                NextAttemptAt = now, // ready immediately; the drain worker picks it up on its next tick
                ChangeRequestId = changeId,
                RecipientActorId = recipientActorId,
            });
        }
    }

    /// <summary>Whether a channel routes the given event. The subscription set is a JSON array of
    /// <see cref="NotificationEventType"/> names; a malformed/empty value routes nothing.</summary>
    public static bool Subscribes(NotificationChannelConfig channel, NotificationEventType eventType)
    {
        if (string.IsNullOrWhiteSpace(channel.EventsJson))
        {
            return false;
        }
        try
        {
            var names = JsonSerializer.Deserialize<List<string>>(channel.EventsJson) ?? [];
            return names.Contains(eventType.ToString(), StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
