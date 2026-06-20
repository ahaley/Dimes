using Dimes.Api;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Providers;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>Tests the stateless Capture Assist chat service with a stub LLM (no network): it returns
/// the assistant reply, requires an Agent actor, and rejects a conversation that doesn't end on a
/// user turn. It must never persist anything.</summary>
public sealed class CaptureAssistServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ProjectService _projects;

    public CaptureAssistServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();
        _projects = new ProjectService(_db, new MembershipResolver(_db));
    }

    private sealed class StubLlm(LlmProviderType type, string text) : ILlmProvider
    {
        public LlmProviderType Type => type;
        public LlmCompletionRequest? Seen { get; private set; }
        public Task<LlmCompletionResult> CompleteAsync(
            LlmCompletionRequest request, LlmConnection connection, CancellationToken ct = default)
        {
            Seen = request;
            return Task.FromResult(new LlmCompletionResult(text));
        }
    }

    private sealed class StubSecrets : ISecretResolver
    {
        public string? Resolve(string? secretRef) => secretRef is null ? null : "resolved";
    }

    private CaptureAssistService Service(StubLlm llm) =>
        new(_db, [llm], new StubSecrets(), new MembershipResolver(_db));

    [Fact]
    public async Task Chat_ReturnsReply_AndReplaysPriorTurnsAsHistory()
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("P", null));
        var llmConfig = await _projects.CreateLlmProviderAsync(project.Id,
            new CreateLlmProviderRequest(LlmProviderType.Anthropic, "claude", null, "claude-sonnet-4-6", "ANTHROPIC_KEY"));
        var agent = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Aria", ActorType.Agent, null, MemberRole.Assistant, llmConfig.Id));

        var stub = new StubLlm(LlmProviderType.Anthropic, "How will users trigger the export?");
        var reply = await Service(stub).ChatAsync(project.Id, new CaptureAssistChatRequest(
            agent.ActorId, "rough idea about CSV export",
            [new ChatTurn("user", "I want CSV export"), new ChatTurn("assistant", "Tell me more"), new ChatTurn("user", "From the board")]));

        Assert.Equal("How will users trigger the export?", reply.Reply);
        // Last user turn is the prompt; the two earlier turns are replayed as history.
        Assert.Equal("From the board", stub.Seen!.User);
        Assert.Equal(2, stub.Seen.History!.Count);
        Assert.Contains("rough idea about CSV export", stub.Seen.System);
    }

    [Fact]
    public async Task Chat_NonAgentActor_IsRejected()
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("P", null));
        var human = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Cory", ActorType.Human, null, MemberRole.Contributor));

        var stub = new StubLlm(LlmProviderType.Anthropic, "x");
        await Assert.ThrowsAsync<BadRequestException>(() => Service(stub).ChatAsync(
            project.Id, new CaptureAssistChatRequest(human.ActorId, null, [new ChatTurn("user", "hi")])));
    }

    [Fact]
    public async Task Chat_ConversationNotEndingOnUser_IsRejected()
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("P", null));
        var llmConfig = await _projects.CreateLlmProviderAsync(project.Id,
            new CreateLlmProviderRequest(LlmProviderType.Anthropic, "claude", null, "claude-sonnet-4-6", "ANTHROPIC_KEY"));
        var agent = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Aria", ActorType.Agent, null, MemberRole.Assistant, llmConfig.Id));

        var stub = new StubLlm(LlmProviderType.Anthropic, "x");
        await Assert.ThrowsAsync<BadRequestException>(() => Service(stub).ChatAsync(
            project.Id, new CaptureAssistChatRequest(agent.ActorId, null, [new ChatTurn("assistant", "hi")])));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
