using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Dimes.Domain;
using Dimes.Domain.Providers;

namespace Dimes.Infrastructure.Providers;

/// <summary>Adapter for any OpenAI-compatible Chat Completions endpoint (<c>POST {baseUrl}/chat/completions</c>):
/// OpenAI itself, or a local runner (Ollama / vLLM / LM Studio). This is the data-stays-local path.
///
/// Ignores <see cref="LlmCompletionRequest.Reasoning"/>: this adapter fronts many unrelated vendors and
/// local runners, and there is no field they agree on — OpenAI has <c>reasoning_effort</c>, Google's shim
/// has neither. Sending a vendor-specific parameter here would 400 on every endpoint that doesn't know it,
/// so reasoning stays at whatever the endpoint's own default is.</summary>
public sealed class OpenAiCompatibleLlmProvider(HttpClient http) : ILlmProvider, ILlmModelCatalog
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
        await LlmHttp.EnsureSuccessAsync(response, "OpenAI-compatible endpoint", ct);

        var body = await response.Content.ReadFromJsonAsync<ChatResponse>(ct);
        var choice = body?.Choices?.FirstOrDefault();
        return new LlmCompletionResult(
            LlmHttp.RequireText(choice?.Message?.Content, "OpenAI-compatible endpoint", choice?.FinishReason));
    }

    /// <summary>Enumerate models via <c>GET {baseUrl}/models</c>. Every endpoint worth pointing this
    /// adapter at implements it — OpenAI, aggregators, Ollama, vLLM, LM Studio — and unlike the vendor
    /// adapters it is unpaged in practice. A runner that lacks the route simply fails the call, which the
    /// caller reports as "this endpoint can't list models" rather than treating it as fatal.
    ///
    /// Errors go through <see cref="LlmHttp.EnsureSuccessAsync"/> for the same reason completions do, and
    /// it matters more here: <c>LlmModelCatalogService</c> puts the exception message straight into the 400
    /// the operator reads on the provider form, so a bare status code turns "invalid API key" into
    /// "401 (Unauthorized)" on the one screen where the mistake is actually being made.</summary>
    public async Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(
        LlmConnection connection, CancellationToken ct = default)
    {
        var baseUrl = (connection.BaseUrl ?? DefaultBaseUrl).TrimEnd('/');
        using var message = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/models");
        if (!string.IsNullOrEmpty(connection.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey);
        }

        using var response = await http.SendAsync(message, ct);
        await LlmHttp.EnsureSuccessAsync(response, "OpenAI-compatible endpoint", ct);

        var body = await response.Content.ReadFromJsonAsync<ModelListResponse>(ct);
        return (body?.Data ?? []).Select(m => new LlmModelInfo(m.Id)).ToList();
    }

    private sealed record ModelListResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<ModelEntry>? Data);

    private sealed record ModelEntry(
        [property: JsonPropertyName("id")] string Id);

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
        [property: JsonPropertyName("message")] ChatMessage? Message,
        // "length" is this API's spelling of "truncated at the token limit"; reported when there is no text.
        [property: JsonPropertyName("finish_reason")] string? FinishReason);
}
