using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Dimes.Domain;
using Dimes.Domain.Providers;

namespace Dimes.Infrastructure.Providers;

/// <summary>Adapter for any OpenAI-compatible Chat Completions endpoint (<c>POST {baseUrl}/chat/completions</c>):
/// OpenAI itself, or a local runner (Ollama / vLLM / LM Studio). This is the data-stays-local path.</summary>
public sealed class OpenAiCompatibleLlmProvider(HttpClient http) : ILlmProvider
{
    private const string DefaultBaseUrl = "https://api.openai.com/v1";

    public LlmProviderType Type => LlmProviderType.OpenAICompatible;

    public async Task<LlmCompletionResult> CompleteAsync(
        LlmCompletionRequest request, LlmConnection connection, CancellationToken ct = default)
    {
        var baseUrl = (connection.BaseUrl ?? DefaultBaseUrl).TrimEnd('/');
        // system, then any prior turns (replayed for context), then the final user message.
        var messages = new List<ChatMessage> { new("system", request.System) };
        messages.AddRange((request.History ?? []).Select(m => new ChatMessage(m.Role, m.Content)));
        messages.Add(new ChatMessage("user", request.User));
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions")
        {
            Content = JsonContent.Create(new ChatRequest(
                connection.Model,
                request.MaxTokens,
                messages)),
        };
        if (!string.IsNullOrEmpty(connection.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey);
        }

        using var response = await http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(ct);
        var text = body?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
        return new LlmCompletionResult(text);
    }

    private sealed record ChatRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages);

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record ChatResponse(
        [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice>? Choices);

    private sealed record ChatChoice(
        [property: JsonPropertyName("message")] ChatMessage? Message);
}
