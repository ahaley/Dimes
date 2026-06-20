using Dimes.Api;
using Dimes.Api.Contracts;
using Dimes.Api.Services;
using Dimes.Domain;
using Dimes.Domain.Lifecycle;
using Dimes.Domain.Providers;
using Dimes.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Tests;

/// <summary>Tests CommentaryService and ScmService with stub providers (no network), confirming
/// recommend-only commentary creates an AgentRecommendation comment without changing state, and SCM
/// context is pulled into the link.</summary>
public sealed class ProviderServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DimesDbContext _db;
    private readonly ProjectService _projects;
    private readonly ChangeRequestService _changes;

    public ProviderServiceTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<DimesDbContext>().UseSqlite(_connection).Options;
        _db = new DimesDbContext(options);
        _db.Database.Migrate();

        var resolver = new MembershipResolver(_db);
        _projects = new ProjectService(_db, resolver);
        _changes = new ChangeRequestService(_db, new LifecycleService(), resolver, new FakeBoardNotifier());
    }

    private sealed class StubLlm(LlmProviderType type, string text) : ILlmProvider
    {
        public LlmProviderType Type => type;
        public Task<LlmCompletionResult> CompleteAsync(
            LlmCompletionRequest request, LlmConnection connection, CancellationToken ct = default)
            => Task.FromResult(new LlmCompletionResult(text));
    }

    private sealed class StubScm(ScmContext? context) : IScmProvider
    {
        public ScmProviderType Type => ScmProviderType.GitHub;
        public Task<ScmContext?> FetchContextAsync(string url, string? token, CancellationToken ct = default)
            => Task.FromResult(context);
    }

    private sealed class StubSecrets : ISecretResolver
    {
        public string? Resolve(string? secretRef) => secretRef is null ? null : "resolved";
    }

    [Fact]
    public async Task AgentCommentary_CreatesRecommendationComment_WithoutChangingState()
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("P", null));
        var human = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Cory", ActorType.Human, null, MemberRole.Contributor));
        var llm = await _projects.CreateLlmProviderAsync(project.Id,
            new CreateLlmProviderRequest(LlmProviderType.Anthropic, "claude", null, "claude-sonnet-4-6", "ANTHROPIC_KEY"));
        var agent = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Aria", ActorType.Agent, null, MemberRole.Contributor, llm.Id));

        var change = await _changes.CreateAsync(project.Id, human.ActorId, new CreateChangeRequest("Improve logging", "Add structured logs", ChangeKind.Feature));

        var commentary = new CommentaryService(
            _db,
            [new StubLlm(LlmProviderType.Anthropic, "Looks reasonable; suggest Medium priority.")],
            new StubSecrets(),
            new MembershipResolver(_db));

        var comment = await commentary.CommentOnChangeAsync(change.Id, agent.ActorId);

        Assert.Equal(CommentKind.AgentRecommendation, comment.Kind);
        Assert.Equal("Looks reasonable; suggest Medium priority.", comment.Body);

        var reloaded = await _changes.GetDetailAsync(change.Id);
        Assert.Equal(ChangeStatus.Captured, reloaded.Change.Status); // unchanged
        Assert.Single(reloaded.Comments);
    }

    [Fact]
    public async Task AgentCommentary_NonAgentActor_IsRejected()
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("P", null));
        var human = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Cory", ActorType.Human, null, MemberRole.Contributor));
        var change = await _changes.CreateAsync(project.Id, human.ActorId, new CreateChangeRequest("x", null, ChangeKind.Problem));

        var commentary = new CommentaryService(
            _db, [new StubLlm(LlmProviderType.Anthropic, "x")], new StubSecrets(), new MembershipResolver(_db));

        await Assert.ThrowsAsync<BadRequestException>(() =>
            commentary.CommentOnChangeAsync(change.Id, human.ActorId));
    }

    [Fact]
    public async Task ScmLink_PullsContext_IntoSnapshot()
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("P", null));
        var human = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Cory", ActorType.Human, null, MemberRole.Contributor));
        var change = await _changes.CreateAsync(project.Id, human.ActorId, new CreateChangeRequest("x", null, ChangeKind.Feature));

        var scm = new ScmService(
            _db,
            [new StubScm(new ScmContext("PR title", "PR body", "open", "PR title\n\nPR body"))],
            new StubSecrets());

        var link = await scm.AddLinkAsync(change.Id,
            new AddScmLinkRequest("https://github.com/acme/widget/pull/7", ContextSnapshot: null));

        Assert.Equal("PR title\n\nPR body", link.ContextSnapshot);
        Assert.Equal(ScmProviderType.GitHub, link.Provider);
    }

    [Fact]
    public async Task ScmLink_ExplicitSnapshot_WinsOverProvider()
    {
        var project = await _projects.CreateAsync(new CreateProjectRequest("P", null));
        var human = await _projects.AddMemberAsync(project.Id,
            new AddMemberRequest("Cory", ActorType.Human, null, MemberRole.Contributor));
        var change = await _changes.CreateAsync(project.Id, human.ActorId, new CreateChangeRequest("x", null, ChangeKind.Feature));

        var scm = new ScmService(
            _db, [new StubScm(new ScmContext("ignored", null, null, "ignored"))], new StubSecrets());

        var link = await scm.AddLinkAsync(change.Id,
            new AddScmLinkRequest("https://github.com/acme/widget/pull/7", ContextSnapshot: "manual note"));

        Assert.Equal("manual note", link.ContextSnapshot);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
