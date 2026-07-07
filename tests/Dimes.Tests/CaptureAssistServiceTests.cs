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

    // ----- Freestyle Mode: markdown brief -> structured proposals (tolerant parse of the LLM reply) -----

    private async Task<Guid> SeedAgentAsync(StubLlm llm)
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("P", null));
        var llmConfig = await _projects.CreateLlmProviderAsync(project.Id,
            new CreateLlmProviderRequest(llm.Type, "claude", null, "claude-sonnet-4-6", "ANTHROPIC_KEY"));
        var agent = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Aria", ActorType.Agent, null, MemberRole.Assistant, llmConfig.Id));
        // Stash both ids on the closure via out-of-band fields isn't needed; return project & agent together.
        _lastAgentId = agent.ActorId;
        return project.Id;
    }
    private Guid _lastAgentId;

    [Fact]
    public async Task GenerateProposals_ParsesPlainJsonArray()
    {
        var stub = new StubLlm(LlmProviderType.Anthropic,
            """[{"title":"Add CSV export","description":"Download the board as CSV.","kind":"Feature","priority":"High"}]""");
        var projectId = await SeedAgentAsync(stub);

        var reply = await Service(stub).GenerateProposalsAsync(projectId,
            new GenerateProposalsRequest(_lastAgentId, "Add CSV export to the board."));

        var p = Assert.Single(reply.Proposals);
        Assert.Equal("Add CSV export", p.Title);
        Assert.Equal("Download the board as CSV.", p.Description);
        Assert.Equal(ChangeKind.Feature, p.Kind);
        Assert.Equal(Priority.High, p.Priority);
    }

    [Fact]
    public async Task GenerateProposals_ToleratesCodeFencesAndSurroundingProse()
    {
        var stub = new StubLlm(LlmProviderType.Anthropic,
            "Sure! Here are the changes:\n```json\n[{\"title\":\"Fix slow inbox\",\"kind\":\"Problem\",\"priority\":\"Medium\"}]\n```\nLet me know!");
        var projectId = await SeedAgentAsync(stub);

        var reply = await Service(stub).GenerateProposalsAsync(projectId,
            new GenerateProposalsRequest(_lastAgentId, "The inbox is slow."));

        var p = Assert.Single(reply.Proposals);
        Assert.Equal("Fix slow inbox", p.Title);
        Assert.Equal(ChangeKind.Problem, p.Kind);
        Assert.Equal(Priority.Medium, p.Priority);
        Assert.Null(p.Description); // missing/blank description maps to null
    }

    [Fact]
    public async Task GenerateProposals_DefaultsBadEnums_AndSkipsTitlelessEntries()
    {
        var stub = new StubLlm(LlmProviderType.Anthropic,
            """[{"title":"Keep me","kind":"Nonsense","priority":"Whatever"},{"title":"","kind":"Feature"}]""");
        var projectId = await SeedAgentAsync(stub);

        var reply = await Service(stub).GenerateProposalsAsync(projectId,
            new GenerateProposalsRequest(_lastAgentId, "Some brief."));

        var p = Assert.Single(reply.Proposals); // the blank-title entry is dropped
        Assert.Equal("Keep me", p.Title);
        Assert.Equal(ChangeKind.Feature, p.Kind);  // unknown kind -> default
        Assert.Equal(Priority.None, p.Priority);    // unknown priority -> default
    }

    [Fact]
    public async Task GenerateProposals_CoercesObservationDrivenToFeature()
    {
        // ObservationDriven is provenance-only (applied by promotion). If the model still emits it, the
        // proposal must land as Feature so a Freestyle batch-create doesn't trip the manual-create guard.
        var stub = new StubLlm(LlmProviderType.Anthropic,
            """[{"title":"From a signal","kind":"ObservationDriven","priority":"Low"}]""");
        var projectId = await SeedAgentAsync(stub);

        var reply = await Service(stub).GenerateProposalsAsync(projectId,
            new GenerateProposalsRequest(_lastAgentId, "Some brief."));

        var p = Assert.Single(reply.Proposals);
        Assert.Equal(ChangeKind.Feature, p.Kind);
    }

    [Fact]
    public async Task GenerateProposals_MalformedJson_ReturnsEmpty()
    {
        var stub = new StubLlm(LlmProviderType.Anthropic, "I couldn't find anything actionable, sorry.");
        var projectId = await SeedAgentAsync(stub);

        var reply = await Service(stub).GenerateProposalsAsync(projectId,
            new GenerateProposalsRequest(_lastAgentId, "Some brief."));

        Assert.Empty(reply.Proposals);
    }

    [Fact]
    public async Task GenerateProposals_BlankMarkdown_ReturnsEmpty_WithoutCallingLlm()
    {
        var stub = new StubLlm(LlmProviderType.Anthropic, "[]");
        var projectId = await SeedAgentAsync(stub);

        var reply = await Service(stub).GenerateProposalsAsync(projectId,
            new GenerateProposalsRequest(_lastAgentId, "   "));

        Assert.Empty(reply.Proposals);
        Assert.Null(stub.Seen); // short-circuited before reaching the provider
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
