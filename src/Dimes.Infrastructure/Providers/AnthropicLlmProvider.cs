using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dimes.Domain;
using Dimes.Domain.Providers;

namespace Dimes.Infrastructure.Providers;

/// <summary>Claude adapter over the Anthropic Messages API (<c>POST /v1/messages</c>).</summary>
public sealed class AnthropicLlmProvider(HttpClient http) : ILlmProvider
{
    private const string DefaultBaseUrl = "https://api.anthropic.com";
    private const string AnthropicVersion = "2023-06-01";

    public LlmProviderType Type => LlmProviderType.Anthropic;

    public async Task<LlmCompletionResult> CompleteAsync(
        LlmCompletionRequest request, LlmConnection connection, CancellationToken ct = default)
    {
        var baseUrl = (connection.BaseUrl ?? DefaultBaseUrl).TrimEnd('/');
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/messages")
        {
            Content = JsonContent.Create(new AnthropicRequest(
                connection.Model,
                request.MaxTokens,
                request.System,
                [new AnthropicMessage("user", request.User)])),
        };
        message.Headers.TryAddWithoutValidation("x-api-key", connection.ApiKey);
        message.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);

        using var response = await http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<AnthropicResponse>(ct);
        var text = body?.Content?.FirstOrDefault(c => c.Type == "text")?.Text ?? string.Empty;
        return new LlmCompletionResult(text);
    }

    private sealed record AnthropicRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("system")] string System,
        [property: JsonPropertyName("messages")] IReadOnlyList<AnthropicMessage> Messages);

    private sealed record AnthropicMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record AnthropicResponse(
        [property: JsonPropertyName("content")] IReadOnlyList<AnthropicContent>? Content);

    private sealed record AnthropicContent(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string? Text);
}
