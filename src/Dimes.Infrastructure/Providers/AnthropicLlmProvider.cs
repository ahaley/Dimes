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
        request = request.WithSettings(connection.Settings);
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
                Thinking(request.Reasoning))),
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

    /// <summary>Translate the requested reasoning mode into the <c>thinking</c> field, or null to omit it.
    ///
    /// <see cref="LlmReasoning.Disabled"/> and <see cref="LlmReasoning.Adaptive"/> are sent explicitly —
    /// see <see cref="LlmReasoning"/> for why the vendor default can't be relied on.
    /// <see cref="LlmReasoning.VendorDefault"/> omits the field, which is the *only* legal request for a
    /// model that reasons unconditionally: Anthropic's Fable/Mythos family rejects
    /// <c>{"type":"disabled"}</c> (and an explicit budget) with a 400. Deliberately keyed off the requested
    /// mode rather than off the model id — a list of always-thinking model names here would go stale on
    /// the next release, and this file's own rule is that a new model needs no code change.
    ///
    /// A model that rejects the mode it is sent still surfaces the vendor's own message via
    /// <see cref="LlmHttp.EnsureSuccessAsync"/> rather than a bare status code, which is what tells the
    /// operator to set the override.</summary>
    private static AnthropicThinking? Thinking(LlmReasoning reasoning) => reasoning switch
    {
        LlmReasoning.VendorDefault => null,
        LlmReasoning.Adaptive => new AnthropicThinking("adaptive"),
        _ => new AnthropicThinking("disabled"),
    };

    /// <summary>Enumerate the models this key can reach (<c>GET /v1/models</c>), so the config UI can
    /// offer live ids rather than a list that goes stale with every release.
    ///
    /// Errors go through <see cref="LlmHttp.EnsureSuccessAsync"/> for the same reason completions do, and
    /// it matters more here: <c>LlmModelCatalogService</c> puts the exception message straight into the 400
    /// the operator reads on the provider form, so a bare status code turns "invalid x-api-key" into
    /// "401 (Unauthorized)" on the one screen where the mistake is actually being made.</summary>
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
            await LlmHttp.EnsureSuccessAsync(response, "Anthropic", ct);

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
        // Omitted rather than sent as null when reasoning is left to the model: the API rejects a null
        // here, and "field absent" is the wire form that means "vendor default".
        [property: JsonPropertyName("thinking"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        AnthropicThinking? Thinking);

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
