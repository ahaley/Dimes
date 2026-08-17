using System.Net;
using System.Text;
using Dimes.Domain.Providers;
using Dimes.Infrastructure.Providers;

namespace Dimes.Tests;

/// <summary>Offline tests for the HTTP provider adapters: request shape (URL, headers, body) and
/// response parsing, using a capturing fake handler — no network.</summary>
public class ProviderAdapterTests
{
    private sealed class CapturingHandler(HttpStatusCode status, string json) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            if (request.Content is not null)
            {
                RequestBody = await request.Content.ReadAsStringAsync(ct);
            }

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    [Fact]
    public async Task Anthropic_PostsToMessagesEndpoint_WithHeaders_AndParsesText()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"content":[{"type":"text","text":"Hello from Claude"}]}""");
        var provider = new AnthropicLlmProvider(new HttpClient(handler));

        var result = await provider.CompleteAsync(
            new LlmCompletionRequest("sys", "hi"),
            new LlmConnection(BaseUrl: null, Model: "claude-sonnet-4-6", ApiKey: "k-123"),
            Ct);

        Assert.Equal("Hello from Claude", result.Text);
        Assert.Equal("https://api.anthropic.com/v1/messages", handler.Request!.RequestUri!.ToString());
        Assert.Equal("k-123", handler.Request.Headers.GetValues("x-api-key").Single());
        Assert.True(handler.Request.Headers.Contains("anthropic-version"));
        Assert.Contains("claude-sonnet-4-6", handler.RequestBody);
        Assert.Contains("max_tokens", handler.RequestBody);
    }

    [Fact]
    public async Task Anthropic_ReplaysHistory_BeforeFinalUserTurn()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"content":[{"type":"text","text":"ok"}]}""");
        var provider = new AnthropicLlmProvider(new HttpClient(handler));

        await provider.CompleteAsync(
            new LlmCompletionRequest("sys", "third", History:
                [new LlmMessage("user", "first"), new LlmMessage("assistant", "second")]),
            new LlmConnection(BaseUrl: null, Model: "claude-sonnet-4-6", ApiKey: "k"),
            Ct);

        // History is replayed in order, ahead of the final user message; the system prompt is a
        // top-level field (not a message), so it should not appear inside the messages array.
        var body = handler.RequestBody!;
        var firstAt = body.IndexOf("first", StringComparison.Ordinal);
        var secondAt = body.IndexOf("second", StringComparison.Ordinal);
        var thirdAt = body.IndexOf("third", StringComparison.Ordinal);
        Assert.True(firstAt >= 0 && secondAt > firstAt && thirdAt > secondAt);
    }

    [Fact]
    public async Task OpenAiCompatible_ReplaysHistory_BetweenSystemAndFinalUserTurn()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"choices":[{"message":{"role":"assistant","content":"ok"}}]}""");
        var provider = new OpenAiCompatibleLlmProvider(new HttpClient(handler));

        await provider.CompleteAsync(
            new LlmCompletionRequest("sysline", "latest", History:
                [new LlmMessage("user", "earlier"), new LlmMessage("assistant", "reply")]),
            new LlmConnection(BaseUrl: "http://localhost:11434/v1", Model: "llama3", ApiKey: null),
            Ct);

        var body = handler.RequestBody!;
        var sysAt = body.IndexOf("sysline", StringComparison.Ordinal);
        var earlierAt = body.IndexOf("earlier", StringComparison.Ordinal);
        var replyAt = body.IndexOf("reply", StringComparison.Ordinal);
        var latestAt = body.IndexOf("latest", StringComparison.Ordinal);
        Assert.True(sysAt >= 0 && earlierAt > sysAt && replyAt > earlierAt && latestAt > replyAt);
    }

    [Fact]
    public async Task OpenAiCompatible_UsesBaseUrl_AndBearer_AndParsesChoice()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"choices":[{"message":{"role":"assistant","content":"Hi from local"}}]}""");
        var provider = new OpenAiCompatibleLlmProvider(new HttpClient(handler));

        var result = await provider.CompleteAsync(
            new LlmCompletionRequest("sys", "hi"),
            new LlmConnection(BaseUrl: "http://localhost:11434/v1", Model: "llama3", ApiKey: "tok"),
            Ct);

        Assert.Equal("Hi from local", result.Text);
        Assert.Equal("http://localhost:11434/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("tok", handler.Request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Anthropic_SendsAnExplicitThinkingMode_RatherThanInheritingTheVendorDefault()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn"}""");
        var provider = new AnthropicLlmProvider(new HttpClient(handler));

        await provider.CompleteAsync(
            new LlmCompletionRequest("sys", "hi", Reasoning: LlmReasoning.Disabled),
            new LlmConnection(BaseUrl: null, Model: "claude-sonnet-4-6", ApiKey: "k"),
            Ct);

        // Omitting the field means "no thinking" on some models and "adaptive" on others, and reasoning is
        // drawn from the same token budget as the answer — so the intent is always stated.
        Assert.Contains("\"thinking\":{\"type\":\"disabled\"}", handler.RequestBody);
    }

    [Fact]
    public async Task Anthropic_AdaptiveReasoning_RequestsAdaptiveThinking()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"content":[{"type":"text","text":"ok"}],"stop_reason":"end_turn"}""");
        var provider = new AnthropicLlmProvider(new HttpClient(handler));

        await provider.CompleteAsync(
            new LlmCompletionRequest("sys", "hi", Reasoning: LlmReasoning.Adaptive),
            new LlmConnection(BaseUrl: null, Model: "claude-sonnet-5", ApiKey: "k"),
            Ct);

        Assert.Contains("\"thinking\":{\"type\":\"adaptive\"}", handler.RequestBody);
    }

    [Fact]
    public async Task Anthropic_ConcatenatesEveryTextBlock_SkippingThinkingBlocks()
    {
        // With reasoning on, thinking blocks lead the content array; citations split the answer across
        // several text blocks. Taking only the first text block would silently drop half the comment.
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """
            {"content":[
              {"type":"thinking","thinking":"deliberating"},
              {"type":"text","text":"Looks reasonable. "},
              {"type":"text","text":"Suggest Medium priority."}
            ],"stop_reason":"end_turn"}
            """);
        var provider = new AnthropicLlmProvider(new HttpClient(handler));

        var result = await provider.CompleteAsync(
            new LlmCompletionRequest("sys", "hi"),
            new LlmConnection(BaseUrl: null, Model: "claude-sonnet-5", ApiKey: "k"),
            Ct);

        Assert.Equal("Looks reasonable. Suggest Medium priority.", result.Text);
    }

    [Fact]
    public async Task Anthropic_TruncatedBeforeAnyText_FailsNamingTheTokenBudget()
    {
        // The failure this guards: reasoning consumes the whole budget and the response carries no text.
        // Storing that as an empty AgentRecommendation looks like the model having nothing to say.
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"content":[{"type":"thinking","thinking":"..."}],"stop_reason":"max_tokens"}""");
        var provider = new AnthropicLlmProvider(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.CompleteAsync(
            new LlmCompletionRequest("sys", "hi", MaxTokens: 1024, Reasoning: LlmReasoning.Adaptive),
            new LlmConnection(BaseUrl: null, Model: "claude-sonnet-5", ApiKey: "k"),
            Ct));

        Assert.Contains("max_tokens", ex.Message);
        Assert.Contains("MaxTokens", ex.Message); // the actionable hint
    }

    [Fact]
    public async Task Anthropic_ErrorResponse_SurfacesApiMessage()
    {
        // A model that rejects the requested thinking mode reports it here; EnsureSuccessStatusCode used to
        // discard this body and leave only a bare status code.
        var handler = new CapturingHandler(HttpStatusCode.BadRequest,
            """{"type":"error","error":{"type":"invalid_request_error","message":"thinking.type: disabled is not supported"}}""");
        var provider = new AnthropicLlmProvider(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.CompleteAsync(
            new LlmCompletionRequest("sys", "hi"),
            new LlmConnection(BaseUrl: null, Model: "some-model", ApiKey: "k"),
            Ct));

        Assert.Contains("disabled is not supported", ex.Message);
    }

    [Fact]
    public async Task OpenAiCompatible_EmptyCompletion_FailsInsteadOfStoringBlankText()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"choices":[{"message":{"role":"assistant","content":""},"finish_reason":"length"}]}""");
        var provider = new OpenAiCompatibleLlmProvider(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.CompleteAsync(
            new LlmCompletionRequest("sys", "hi"),
            new LlmConnection(BaseUrl: "http://localhost:11434/v1", Model: "llama3", ApiKey: null),
            Ct));

        Assert.Contains("length", ex.Message);
        Assert.Contains("MaxTokens", ex.Message);
    }

    [Fact]
    public async Task Gemini_PostsToGenerateContent_WithApiKeyHeader_AndParsesText()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"candidates":[{"content":{"role":"model","parts":[{"text":"Hello from "},{"text":"Gemini"}]}}]}""");
        var provider = new GeminiLlmProvider(new HttpClient(handler));

        var result = await provider.CompleteAsync(
            new LlmCompletionRequest("sys", "hi"),
            new LlmConnection(BaseUrl: null, Model: "gemini-x", ApiKey: "k-123"),
            Ct);

        // All text parts of the candidate are concatenated, not just the first.
        Assert.Equal("Hello from Gemini", result.Text);
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-x:generateContent",
            handler.Request!.RequestUri!.ToString());
        // The key must travel as a header — a '?key=' would land in request and proxy logs.
        Assert.Equal("k-123", handler.Request.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain("k-123", handler.Request.RequestUri.ToString());
    }

    [Fact]
    public async Task Gemini_MapsAssistantTurnsToModelRole_AndKeepsSystemInstructionSeparate()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"candidates":[{"content":{"parts":[{"text":"ok"}]}}]}""");
        var provider = new GeminiLlmProvider(new HttpClient(handler));

        await provider.CompleteAsync(new LlmCompletionRequest("sysline", "latest", History:
                [new LlmMessage("user", "earlier"), new LlmMessage("assistant", "reply")]), new LlmConnection(BaseUrl: null, Model: "gemini-x", ApiKey: "k"), Ct);

        var body = handler.RequestBody!;
        // Gemini spells the assistant side "model"; sending "assistant" is rejected outright, so this
        // mapping is what keeps multi-turn Capture Assist working.
        Assert.Contains("\"role\":\"model\"", body);
        Assert.DoesNotContain("assistant", body);
        // The system prompt is a top-level systemInstruction, not a message in contents.
        Assert.Contains("systemInstruction", body);

        var sysAt = body.IndexOf("sysline", StringComparison.Ordinal);
        var earlierAt = body.IndexOf("earlier", StringComparison.Ordinal);
        var replyAt = body.IndexOf("reply", StringComparison.Ordinal);
        var latestAt = body.IndexOf("latest", StringComparison.Ordinal);
        Assert.True(sysAt >= 0 && earlierAt > sysAt && replyAt > earlierAt && latestAt > replyAt);
    }

    [Fact]
    public async Task Gemini_NoTextReturned_ReportsWhy_RatherThanEmptyComment()
    {
        // A safety-blocked prompt returns 200 with no candidates. Storing that as an empty agent
        // recommendation would look like the model had nothing to say.
        var handler = new CapturingHandler(HttpStatusCode.OK, """{"promptFeedback":{"blockReason":"SAFETY"}}""");
        var provider = new GeminiLlmProvider(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.CompleteAsync(
            new LlmCompletionRequest("sys", "hi"),
            new LlmConnection(BaseUrl: null, Model: "gemini-x", ApiKey: "k"),
            Ct));

        Assert.Contains("SAFETY", ex.Message);
    }

    [Fact]
    public async Task Gemini_ErrorResponse_SurfacesApiMessage()
    {
        var handler = new CapturingHandler(HttpStatusCode.BadRequest,
            """{"error":{"code":400,"message":"API key not valid. Please pass a valid API key."}}""");
        var provider = new GeminiLlmProvider(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.CompleteAsync(
            new LlmCompletionRequest("sys", "hi"),
            new LlmConnection(BaseUrl: null, Model: "gemini-x", ApiKey: "bad"),
            Ct));

        // A bare status code leaves the operator guessing; the API's own message names the fix.
        Assert.Contains("API key not valid", ex.Message);
    }

    /// <summary>Model discovery is the interactive surface — LlmModelCatalogService puts the exception
    /// message straight into the 400 the operator reads on the provider form — so a failed listing must
    /// carry the vendor's own text, exactly as a failed completion does. Both adapters previously used
    /// EnsureSuccessStatusCode here, which reports "401 (Unauthorized)" and discards the reason.</summary>
    [Fact]
    public async Task Anthropic_ListModelsError_SurfacesApiMessage_NotABareStatusCode()
    {
        var handler = new CapturingHandler(HttpStatusCode.Unauthorized,
            """{"error":{"type":"authentication_error","message":"invalid x-api-key"}}""");
        var provider = new AnthropicLlmProvider(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.ListModelsAsync(
            new LlmConnection(BaseUrl: null, Model: string.Empty, ApiKey: "bad"), Ct));

        Assert.Contains("invalid x-api-key", ex.Message);
    }

    [Fact]
    public async Task OpenAiCompatible_ListModelsError_SurfacesApiMessage_NotABareStatusCode()
    {
        var handler = new CapturingHandler(HttpStatusCode.NotFound,
            """{"error":{"message":"Unknown route /models"}}""");
        var provider = new OpenAiCompatibleLlmProvider(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => provider.ListModelsAsync(
            new LlmConnection(BaseUrl: "https://runner.local/v1", Model: string.Empty, ApiKey: null), Ct));

        Assert.Contains("Unknown route /models", ex.Message);
    }

    [Fact]
    public async Task Gemini_ListModels_KeepsGenerativeModels_AndStripsResourcePrefix()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """
            {"models":[
              {"name":"models/gemini-x","displayName":"Gemini X","supportedGenerationMethods":["generateContent"]},
              {"name":"models/embedding-001","displayName":"Embed","supportedGenerationMethods":["embedContent"]}
            ]}
            """);
        var provider = new GeminiLlmProvider(new HttpClient(handler));

        var models = await provider.ListModelsAsync(new LlmConnection(BaseUrl: null, Model: string.Empty, ApiKey: "k"), Ct);

        // Embedding-only models would just 400 if selected for commentary, so they are filtered out; the
        // id offered is the leaf, which is what goes in LlmProviderConfig.Model.
        var model = Assert.Single(models);
        Assert.Equal("gemini-x", model.Id);
        Assert.Equal("Gemini X", model.DisplayName);
        Assert.Contains("/v1beta/models", handler.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task GeminiVertex_WithoutProjectOrLocation_FailsBeforeAnyRequest()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "{}");
        var provider = new GeminiVertexLlmProvider(new HttpClient(handler));

        // Vertex addresses a model by project + region; without them there is no URL to build. Failing
        // here (rather than assembling a malformed one) is why the save-time check has a runtime twin.
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new LlmCompletionRequest("sys", "hi"), new LlmConnection(
                BaseUrl: null, Model: "gemini-x", ApiKey: "{}",
                Settings: new LlmProviderSettings { GcpLocation = "us-central1" }), Ct));

        Assert.Null(handler.Request);
    }

    /// <summary>Drives CompleteAsync for a Vertex provider whose credential reference resolved to
    /// <paramref name="credential"/>, and returns the resulting error.
    ///
    /// Every case here fails before any token is minted (that needs a real RSA key and Google's token
    /// endpoint), so the *message* is what distinguishes them — and it is enough: a "not valid
    /// service-account JSON" parse error proves the value was read as key material, while the
    /// "neither JSON nor a path" error proves it was rejected before any file was touched.</summary>
    private static async Task<InvalidOperationException> VertexCredentialFailure(string credential)
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "{}");
        var provider = new GeminiVertexLlmProvider(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(
            new LlmCompletionRequest("sys", "hi"),
            new LlmConnection(
                BaseUrl: null, Model: "gemini-x", ApiKey: credential,
                Settings: new LlmProviderSettings { GcpProject = "p", GcpLocation = "us-central1" })));

        Assert.Null(handler.Request); // never reached the network
        return ex;
    }

    [Fact]
    public async Task GeminiVertex_CredentialThatIsAPath_ReadsTheKeyFileFromDisk()
    {
        // Operators hold a service-account key as a file, so binding the reference to its path is the
        // natural thing to do — a plain environment variable holding the path now works, not just the
        // explicit SecretFiles:/_FILE routes.
        var path = Path.Combine(Path.GetTempPath(), $"dimes-vertex-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"type":"service_account","project_id":"p"}""");
        try
        {
            var ex = await VertexCredentialFailure(path);

            // Reaching the Google parser at all proves the path was resolved and the file's contents were
            // used as key material — a value rejected as "not a path" never gets this far.
            Assert.Contains("not a valid Google service-account credentials JSON", ex.Message);
            Assert.DoesNotContain("absolute file path", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task GeminiVertex_CredentialPathThatCannotBeRead_NamesThePath()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"dimes-absent-{Guid.NewGuid():N}.json");

        var ex = await VertexCredentialFailure(missing);

        Assert.Contains(missing, ex.Message);
        Assert.Contains("could not be read", ex.Message);
    }

    [Theory]
    [InlineData("sk-ant-api03-notarealkey")] // an API key pasted into the credentials field
    [InlineData("creds/vertex.json")]        // relative — would resolve against the host's working directory
    public async Task GeminiVertex_CredentialThatIsNeitherJsonNorAnAbsolutePath_IsRefusedWithoutEchoingIt(
        string credential)
    {
        var ex = await VertexCredentialFailure(credential);

        Assert.Contains("neither service-account JSON nor an absolute file path", ex.Message);
        // The value must never reach the message: a key pasted here would otherwise be written into logs.
        Assert.DoesNotContain(credential, ex.Message);
    }

    [Fact]
    public async Task GitHub_ParsesPrUrl_CallsIssuesApi_AndReturnsContext()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"title":"Fix bug","body":"Steps to repro","state":"open"}""");
        var provider = new GitHubScmProvider(new HttpClient(handler));

        var context = await provider.FetchContextAsync("https://github.com/acme/widget/pull/42", token: "ghp_x", ct: Ct);

        Assert.NotNull(context);
        Assert.Equal("Fix bug", context!.Title);
        Assert.Equal("open", context.State);
        Assert.Equal("https://api.github.com/repos/acme/widget/issues/42", handler.Request!.RequestUri!.ToString());
        Assert.True(handler.Request.Headers.UserAgent.Count > 0);
        Assert.Equal("ghp_x", handler.Request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task GitHub_UnrecognizedUrl_ReturnsNull_WithoutCallingHttp()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, "{}");
        var provider = new GitHubScmProvider(new HttpClient(handler));

        var context = await provider.FetchContextAsync("https://example.com/not-github", token: null, ct: Ct);

        Assert.Null(context);
        Assert.Null(handler.Request); // never hit the network
    }
}
