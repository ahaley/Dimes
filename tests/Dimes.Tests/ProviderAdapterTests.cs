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
            new LlmConnection(BaseUrl: null, Model: "claude-sonnet-4-6", ApiKey: "k-123"));

        Assert.Equal("Hello from Claude", result.Text);
        Assert.Equal("https://api.anthropic.com/v1/messages", handler.Request!.RequestUri!.ToString());
        Assert.Equal("k-123", handler.Request.Headers.GetValues("x-api-key").Single());
        Assert.True(handler.Request.Headers.Contains("anthropic-version"));
        Assert.Contains("claude-sonnet-4-6", handler.RequestBody);
        Assert.Contains("max_tokens", handler.RequestBody);
    }

    [Fact]
    public async Task OpenAiCompatible_UsesBaseUrl_AndBearer_AndParsesChoice()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"choices":[{"message":{"role":"assistant","content":"Hi from local"}}]}""");
        var provider = new OpenAiCompatibleLlmProvider(new HttpClient(handler));

        var result = await provider.CompleteAsync(
            new LlmCompletionRequest("sys", "hi"),
            new LlmConnection(BaseUrl: "http://localhost:11434/v1", Model: "llama3", ApiKey: "tok"));

        Assert.Equal("Hi from local", result.Text);
        Assert.Equal("http://localhost:11434/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("tok", handler.Request.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task GitHub_ParsesPrUrl_CallsIssuesApi_AndReturnsContext()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK,
            """{"title":"Fix bug","body":"Steps to repro","state":"open"}""");
        var provider = new GitHubScmProvider(new HttpClient(handler));

        var context = await provider.FetchContextAsync("https://github.com/acme/widget/pull/42", token: "ghp_x");

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

        var context = await provider.FetchContextAsync("https://example.com/not-github", token: null);

        Assert.Null(context);
        Assert.Null(handler.Request); // never hit the network
    }
}
