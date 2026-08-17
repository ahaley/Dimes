using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dimes.Domain;
using Dimes.Domain.Providers;

namespace Dimes.Infrastructure.Providers;

/// <summary>Claude adapter over the Anthropic Messages API (<c>POST /v1/messages</c>).</summary>
public sealed class AnthropicLlmProvider(HttpClient http) : ILlmProvider, ILlmModelCatalog
{
    private const string DefaultBaseUrl = "https://api.anthropic.com";
    private const string AnthropicVersion = "2023-06-01";

    /// <summary>Runaway guard on the paged model listing, not a real limit.</summary>
    private const int MaxModelPages = 5;

    public LlmProviderType Type => LlmProviderType.Anthropic;

    public async Task<LlmCompletionResult> CompleteAsync(
        LlmCompletionRequest request, LlmConnection connection, CancellationToken ct = default)
    {
        var baseUrl = (connection.BaseUrl ?? DefaultBaseUrl).TrimEnd('/');
        // Replay prior turns (if any) ahead of the final user message so the model has the
        // conversation context; a null/empty history collapses to a one-shot call.
        var messages = (request.History ?? [])
            .Select(m => new AnthropicMessage(m.Role, m.Content))
            .Append(new AnthropicMessage("user", request.User))
            .ToList();
        using var message = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/v1/messages")
        {
            Content = JsonContent.Create(new AnthropicRequest(
                connection.Model,
                request.MaxTokens,
                request.System,
                messages,
                new AnthropicThinking(ThinkingType(request.Reasoning)))),
        };
        message.Headers.TryAddWithoutValidation("x-api-key", connection.ApiKey);
        message.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);

        using var response = await http.SendAsync(message, ct);
        await LlmHttp.EnsureSuccessAsync(response, "Anthropic", ct);

        var body = await response.Content.ReadFromJsonAsync<AnthropicResponse>(ct);
        // Concatenate every text block: a response can lead with thinking blocks, and citations split the
        // answer across several text blocks — taking only the first would silently drop content.
        var text = string.Concat(
            (body?.Content ?? []).Where(c => c.Type == "text").Select(c => c.Text));
        return new LlmCompletionResult(LlmHttp.RequireText(text, "Anthropic", body?.StopReason));
    }

    /// <summary>Always send an explicit <c>thinking</c> mode — see <see cref="LlmReasoning"/> for why the
    /// vendor default can't be relied on. A model that rejects the requested mode surfaces its own error
    /// message via <see cref="LlmHttp.EnsureSuccessAsync"/> rather than a bare status code.</summary>
    private static string ThinkingType(LlmReasoning reasoning) =>
        reasoning == LlmReasoning.Adaptive ? "adaptive" : "disabled";

    /// <summary>Enumerate the models this key can reach (<c>GET /v1/models</c>), so the config UI can
    /// offer live ids rather than a list that goes stale with every release.</summary>
    public async Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(
        LlmConnection connection, CancellationToken ct = default)
    {
        var baseUrl = (connection.BaseUrl ?? DefaultBaseUrl).TrimEnd('/');
        var models = new List<LlmModelInfo>();
        string? afterId = null;

        for (var page = 0; page < MaxModelPages; page++)
        {
            var url = $"{baseUrl}/v1/models?limit=100";
            if (afterId is not null)
            {
                url += $"&after_id={Uri.EscapeDataString(afterId)}";
            }

            using var message = new HttpRequestMessage(HttpMethod.Get, url);
            message.Headers.TryAddWithoutValidation("x-api-key", connection.ApiKey);
            message.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);

            using var response = await http.SendAsync(message, ct);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<ModelListResponse>(ct);
            models.AddRange((body?.Data ?? []).Select(m => new LlmModelInfo(m.Id, m.DisplayName)));

            if (body?.HasMore != true || body.LastId is null)
            {
                break;
            }
            afterId = body.LastId;
        }

        return models;
    }

    private sealed record ModelListResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<ModelEntry>? Data,
        [property: JsonPropertyName("has_more")] bool? HasMore,
        [property: JsonPropertyName("last_id")] string? LastId);

    private sealed record ModelEntry(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("display_name")] string? DisplayName);

    private sealed record AnthropicRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("system")] string System,
        [property: JsonPropertyName("messages")] IReadOnlyList<AnthropicMessage> Messages,
        [property: JsonPropertyName("thinking")] AnthropicThinking Thinking);

    private sealed record AnthropicThinking(
        [property: JsonPropertyName("type")] string Type);

    private sealed record AnthropicMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record AnthropicResponse(
        [property: JsonPropertyName("content")] IReadOnlyList<AnthropicContent>? Content,
        // Names why generation stopped — "max_tokens" when the budget ran out, "refusal" on a safety
        // decline. Reported when there is no text so the cause isn't guesswork.
        [property: JsonPropertyName("stop_reason")] string? StopReason);

    private sealed record AnthropicContent(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string? Text);
}
