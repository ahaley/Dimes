using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Domain.Providers;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dimes.Tests;

/// <summary>The outbox drain: it delivers pending rows through the channel adapter and records the outcome.
/// The retry/backoff and health-stamping are the whole point — a dead endpoint must become visible (a
/// last-delivery error on the channel) and eventually give up, never loop forever or fail silently.</summary>
public sealed class NotificationDrainTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly FakeChannelProvider _provider = new();

    public NotificationDrainTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();
    }

    private NotificationDrainRunner Runner() => new(
        _db, new INotificationChannelProvider[] { _provider }, new FixedSecretResolver(),
        NullLogger<NotificationDrainRunner>.Instance);

    private async Task<(NotificationChannelConfig Channel, NotificationDelivery Delivery)> SeedAsync(
        DateTimeOffset? nextAttemptAt = null)
    {
        var project = new Project { Name = "Demo", Key = "DEMO" };
        _db.Projects.Add(project);
        var channel = new NotificationChannelConfig
        {
            ProjectId = project.Id, Type = NotificationChannelType.GoogleChat, Name = "team",
            Target = "spaces/AAAA", SecretRef = "GCHAT_CREDS", EventsJson = "[\"AwaitingApproval\"]",
        };
        _db.NotificationChannelConfigs.Add(channel);
        var delivery = new NotificationDelivery
        {
            ProjectId = project.Id, ChannelConfigId = channel.Id, Event = NotificationEventType.AwaitingApproval,
            Title = "Change awaiting approval", Body = "DEMO-1 — Do the thing is now Triaged.",
            Status = NotificationDeliveryStatus.Pending, NextAttemptAt = nextAttemptAt ?? DateTimeOffset.UtcNow.AddSeconds(-1),
        };
        _db.NotificationDeliveries.Add(delivery);
        await _db.SaveChangesAsync();
        return (channel, delivery);
    }

    [Fact]
    public async Task Drain_DeliversPending_MarksSent_AndStampsHealth()
    {
        var (channel, delivery) = await SeedAsync();

        var count = await Runner().RunOnceAsync();

        Assert.Equal(1, count);
        Assert.Single(_provider.Sent);
        Assert.Equal("Change awaiting approval", _provider.Sent[0].Title);

        await _db.Entry(delivery).ReloadAsync();
        await _db.Entry(channel).ReloadAsync();
        Assert.Equal(NotificationDeliveryStatus.Sent, delivery.Status);
        Assert.Equal(1, delivery.Attempts);
        Assert.True(channel.LastDeliveryOk);
        Assert.Null(channel.LastDeliveryError);
    }

    [Fact]
    public async Task Drain_OnFailure_RetriesWithBackoff_AndSurfacesError()
    {
        _provider.FailWith = "space not found";
        var (channel, delivery) = await SeedAsync();

        await Runner().RunOnceAsync();

        await _db.Entry(delivery).ReloadAsync();
        await _db.Entry(channel).ReloadAsync();
        // Not terminal yet: it stays Pending, scheduled for a future retry.
        Assert.Equal(NotificationDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(1, delivery.Attempts);
        Assert.NotNull(delivery.NextAttemptAt);
        Assert.True(delivery.NextAttemptAt > DateTimeOffset.UtcNow);
        // The failure is visible on the channel for the settings health badge.
        Assert.False(channel.LastDeliveryOk);
        Assert.Contains("space not found", channel.LastDeliveryError);
    }

    [Fact]
    public async Task Drain_GivesUpAfterMaxAttempts_MarksFailed()
    {
        _provider.FailWith = "still broken";
        var (_, delivery) = await SeedAsync();
        // One attempt short of the cap — the next failure is terminal.
        delivery.Attempts = NotificationDrainRunner.MaxAttempts - 1;
        await _db.SaveChangesAsync();

        await Runner().RunOnceAsync();

        await _db.Entry(delivery).ReloadAsync();
        Assert.Equal(NotificationDeliveryStatus.Failed, delivery.Status);
        Assert.Equal(NotificationDrainRunner.MaxAttempts, delivery.Attempts);
    }

    [Fact]
    public async Task Drain_SkipsDeliveriesNotYetDue()
    {
        var (_, delivery) = await SeedAsync(nextAttemptAt: DateTimeOffset.UtcNow.AddMinutes(10));

        var count = await Runner().RunOnceAsync();

        Assert.Equal(0, count);
        Assert.Empty(_provider.Sent);
        await _db.Entry(delivery).ReloadAsync();
        Assert.Equal(0, delivery.Attempts);
    }

    private sealed class FakeChannelProvider : INotificationChannelProvider
    {
        public List<NotificationMessage> Sent { get; } = new();
        public string? FailWith { get; set; }
        public NotificationChannelType Type => NotificationChannelType.GoogleChat;

        public Task SendAsync(NotificationMessage message, NotificationConnection connection, CancellationToken ct = default)
        {
            if (FailWith is not null)
            {
                throw new HttpRequestException(FailWith);
            }
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedSecretResolver : ISecretResolver
    {
        public string? Resolve(string? secretRef) => secretRef is null ? null : "{\"type\":\"service_account\"}";
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
