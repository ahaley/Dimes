using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Domain.Providers;
using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Services;

/// <summary>Drains the notification outbox: pulls due deliveries, sends each through the channel's adapter,
/// and records the outcome — success settles the row and stamps the channel's last-delivery health; failure
/// retries with exponential backoff until <see cref="MaxAttempts"/>, after which the row is <c>Failed</c> and
/// the channel shows a last-delivery error (so a dead endpoint surfaces in settings rather than failing
/// silently forever). The only writer of delivery outcomes. Scoped so it can be unit-tested directly with a
/// fake channel adapter, while <see cref="NotificationDrainService"/> just ticks it on a timer.</summary>
public sealed class NotificationDrainRunner(
    DimesDbContext db,
    IEnumerable<INotificationChannelProvider> providers,
    ISecretResolver secrets,
    ILogger<NotificationDrainRunner> logger)
{
    public const int BatchSize = 50;
    public const int MaxAttempts = 6;

    /// <summary>Drain one batch of due deliveries. Returns how many were attempted.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var due = await db.NotificationDeliveries
            .Include(d => d.ChannelConfig)
            .Where(d => d.Status == NotificationDeliveryStatus.Pending
                && (d.NextAttemptAt == null || d.NextAttemptAt <= now))
            .OrderBy(d => d.NextAttemptAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (due.Count == 0)
        {
            return 0;
        }

        foreach (var delivery in due)
        {
            await AttemptAsync(delivery, ct);
        }

        await db.SaveChangesAsync(ct);
        return due.Count;
    }

    private async Task AttemptAsync(NotificationDelivery delivery, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        delivery.Attempts++;
        delivery.LastAttemptAt = now;

        var channel = delivery.ChannelConfig;
        try
        {
            if (channel is null)
            {
                throw new InvalidOperationException("The delivery's channel no longer exists.");
            }

            var provider = providers.FirstOrDefault(p => p.Type == channel.Type)
                ?? throw new InvalidOperationException($"No adapter for channel type '{channel.Type}'.");

            var connection = new NotificationConnection(channel.Target, secrets.Resolve(channel.SecretRef));
            await provider.SendAsync(new NotificationMessage(delivery.Title, delivery.Body), connection, ct);

            delivery.Status = NotificationDeliveryStatus.Sent;
            delivery.LastError = null;
            StampChannel(channel, now, ok: true, error: null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            delivery.LastError = ex.Message;
            if (delivery.Attempts >= MaxAttempts)
            {
                delivery.Status = NotificationDeliveryStatus.Failed;
                logger.LogWarning(
                    "Notification delivery {DeliveryId} to channel {ChannelId} failed permanently after {Attempts} attempts: {Error}",
                    delivery.Id, delivery.ChannelConfigId, delivery.Attempts, ex.Message);
            }
            else
            {
                delivery.NextAttemptAt = now + Backoff(delivery.Attempts);
            }
            // Stamp the channel's health even on a non-terminal failure so operators see it going wrong early.
            StampChannel(channel, now, ok: false, error: ex.Message);
        }
    }

    private static void StampChannel(NotificationChannelConfig? channel, DateTimeOffset at, bool ok, string? error)
    {
        if (channel is null)
        {
            return;
        }
        channel.LastDeliveryAt = at;
        channel.LastDeliveryOk = ok;
        channel.LastDeliveryError = error;
    }

    /// <summary>Exponential backoff: 30s, 1m, 2m, 4m, 8m … capped at 30m.</summary>
    private static TimeSpan Backoff(int attempts)
    {
        var seconds = 30 * Math.Pow(2, attempts - 1);
        return TimeSpan.FromSeconds(Math.Min(seconds, 30 * 60));
    }
}

/// <summary>Ticks <see cref="NotificationDrainRunner"/> every ~15s in its own DI scope.</summary>
public sealed class NotificationDrainService(
    IServiceScopeFactory scopeFactory, ILogger<NotificationDrainService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<NotificationDrainRunner>();
                await runner.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A batch-level failure (e.g. a transient DB error) must not kill the loop.
                logger.LogError(ex, "Notification drain pass failed; will retry next tick.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
