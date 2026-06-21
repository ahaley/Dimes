using Dimes.Api;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Domain.Lifecycle;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>Covers the persisted human Capture Assist conversation: starting bubbles a directed
/// observation into the assistant's inbox, the assistant's reply flips status and dismisses that
/// observation through the lifecycle engine, and the participant/eligibility guards hold.</summary>
public sealed class AssistConversationServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ProjectService _projects;
    private readonly AssistConversationService _assist;
    private readonly FakeBoardNotifier _notifier = new();

    public AssistConversationServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();

        var lifecycle = new LifecycleService();
        var resolver = new MembershipResolver(_db);
        _projects = new ProjectService(_db, resolver);
        _assist = new AssistConversationService(_db, resolver, lifecycle, _notifier);
    }

    private async Task<(Guid ProjectId, Guid RequesterId, Guid AssistantId, Guid ReporterId, Guid AgentId)> SeedAsync()
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("Demo", null));
        var requester = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Cory", ActorType.Human, "cory@x.com", MemberRole.Contributor));
        var assistant = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Maud", ActorType.Human, "maud@x.com", MemberRole.Maintainer));
        var reporter = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Rhea", ActorType.Human, "rhea@x.com", MemberRole.Reporter));
        var agent = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Aria", ActorType.Agent, null, MemberRole.Assistant));
        return (project.Id, requester.ActorId, assistant.ActorId, reporter.ActorId, agent.ActorId);
    }

    [Fact]
    public async Task Start_CreatesConversation_AndDirectedInboxObservation()
    {
        var seed = await SeedAsync();

        var convo = await _assist.StartAsync(seed.ProjectId, seed.RequesterId,
            new StartAssistConversationRequest(seed.AssistantId, "Rough draft", "CSV export", "Can you help me scope this?"));

        Assert.Equal(AssistConversationStatus.AwaitingAssistant, convo.Status);
        var message = Assert.Single(convo.Messages);
        Assert.Equal(AssistMessageSender.Requester, message.Sender);

        // Bubble-up: a directed AssistRequest observation in the New state, addressed to the assistant.
        var obs = Assert.Single(_db.Observations.Where(o => o.ProjectId == seed.ProjectId));
        Assert.Equal(ObservationKind.AssistRequest, obs.Kind);
        Assert.Equal(ObservationStatus.New, obs.Status);
        Assert.Equal(seed.AssistantId, obs.TargetActorId);

        // It came from an auto-provisioned internal source.
        var source = await _db.ObservationSources.FindAsync(obs.SourceId);
        Assert.Equal(ObservationSourceType.Internal, source!.Type);
    }

    [Fact]
    public async Task AssistantReply_FlipsStatus_AndDismissesInboxObservation()
    {
        var seed = await SeedAsync();
        var convo = await _assist.StartAsync(seed.ProjectId, seed.RequesterId,
            new StartAssistConversationRequest(seed.AssistantId, null, null, "Need help"));

        var updated = await _assist.PostMessageAsync(seed.ProjectId, convo.Id, seed.AssistantId,
            new PostAssistMessageRequest("Happy to help — what's the goal?"));

        Assert.Equal(AssistConversationStatus.AwaitingRequester, updated.Status);
        Assert.Equal(2, updated.Messages.Count);

        var obs = Assert.Single(_db.Observations.Where(o => o.ProjectId == seed.ProjectId));
        Assert.Equal(ObservationStatus.Dismissed, obs.Status);
    }

    [Fact]
    public async Task PostMessage_ByNonParticipant_IsForbidden()
    {
        var seed = await SeedAsync();
        var convo = await _assist.StartAsync(seed.ProjectId, seed.RequesterId,
            new StartAssistConversationRequest(seed.AssistantId, null, null, "Need help"));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _assist.PostMessageAsync(seed.ProjectId, convo.Id, seed.ReporterId, new PostAssistMessageRequest("butting in")));
    }

    [Fact]
    public async Task Start_WithAgentAssistant_IsRejected()
    {
        var seed = await SeedAsync();

        await Assert.ThrowsAsync<BadRequestException>(() =>
            _assist.StartAsync(seed.ProjectId, seed.RequesterId,
                new StartAssistConversationRequest(seed.AgentId, null, null, "Need help")));
    }

    [Fact]
    public async Task Start_WithReporterAssistant_IsRejected()
    {
        var seed = await SeedAsync();

        // A Reporter can't clear the bubble-up observation through the lifecycle guard, so they're
        // ineligible as a human assistant.
        await Assert.ThrowsAsync<BadRequestException>(() =>
            _assist.StartAsync(seed.ProjectId, seed.RequesterId,
                new StartAssistConversationRequest(seed.ReporterId, null, null, "Need help")));
    }

    [Fact]
    public async Task PendingList_ForAssistant_ShowsAwaitingRequests()
    {
        var seed = await SeedAsync();
        await _assist.StartAsync(seed.ProjectId, seed.RequesterId,
            new StartAssistConversationRequest(seed.AssistantId, null, "CSV export", "Need help"));

        var pending = await _assist.ListAsync(seed.ProjectId, seed.AssistantId, "assistant", AssistConversationStatus.AwaitingAssistant, default);

        var summary = Assert.Single(pending);
        Assert.Equal("Cory", summary.RequesterName);
        Assert.Equal(1, summary.MessageCount);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
