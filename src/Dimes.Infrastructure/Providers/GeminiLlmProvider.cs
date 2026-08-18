using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Dimes.Domain;
using Dimes.Domain.Providers;

namespace Dimes.Infrastructure.Providers;

/// <summary>Gemini adapter over the Google AI (AI Studio) API
/// (<c>POST /v1beta/models/{model}:generateContent</c>), authenticating with a bring-your-own API key in
/// the <c>x-goog-api-key</c> header.
///
/// Gemini is also reachable through <see cref="OpenAiCompatibleLlmProvider"/> pointed at Google's
/// compatibility shim, and that remains a supported configuration. This adapter exists because the shim
/// is a subset: it has no place for Gemini-only request options, and it hides the role/system-instruction
/// differences that <see cref="GeminiGenerateContent"/> handles explicitly.</summary>
public sealed class GeminiLlmProvider(HttpClient http) : ILlmProvider, ILlmModelCatalog
{
    private const string DefaultBaseUrl = "https://generativelanguage.googleapis.com";
    private const string ApiVersion = "v1beta";

    /// <summary>Model listings are paged. The bound is a runaway guard, not a real limit — a few hundred
    /// models per page is far more than the catalog holds.</summary>
    private const int MaxModelPages = 5;

    public LlmProviderType Type => LlmProviderType.Gemini;

    public async Task<LlmCompletionResult> CompleteAsync(
        LlmCompletionRequest request, LlmConnection connection, CancellationToken ct = default)
    {
        // Picks up the token-budget override; the reasoning one is refused at save time for this type.
        request = request.WithSettings(connection.Settings);
        string baseUrl = (connection.BaseUrl ?? DefaultBaseUrl).TrimEnd('/');
        using HttpRequestMessage message = new(
            HttpMethod.Post, $"{baseUrl}/{ApiVersion}/models/{Uri.EscapeDataString(connection.Model)}:generateContent")
        {
            Content = JsonContent.Create(GeminiGenerateContent.BuildRequest(request)),
        };
        // The key goes in a header, never the query string: a '?key=' would be echoed into request logs
        // and proxy access logs along the whole path.
        message.Headers.TryAddWithoutValidation("x-goog-api-key", connection.ApiKey);

        using HttpResponseMessage response = await http.SendAsync(message, ct);
        await LlmHttp.EnsureSuccessAsync(response, "Gemini", ct);

        GeminiGenerateContent.GeminiResponse? body =
            await response.Content.ReadFromJsonAsync<GeminiGenerateContent.GeminiResponse>(ct);
        return new LlmCompletionResult(GeminiGenerateContent.ExtractText(body));
    }

    public async Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(
        LlmConnection connection, CancellationToken ct = default)
    {
        string baseUrl = (connection.BaseUrl ?? DefaultBaseUrl).TrimEnd('/');
        List<LlmModelInfo> models = [];
        string? pageToken = null;

        for (int page = 0; page < MaxModelPages; page++)
        {
            string url = $"{baseUrl}/{ApiVersion}/models?pageSize=200";
            if (pageToken is not null)
            {
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            using HttpRequestMessage message = new(HttpMethod.Get, url);
            message.Headers.TryAddWithoutValidation("x-goog-api-key", connection.ApiKey);

            using HttpResponseMessage response = await http.SendAsync(message, ct);
            await LlmHttp.EnsureSuccessAsync(response, "Gemini", ct);

            ModelListResponse? body = await response.Content.ReadFromJsonAsync<ModelListResponse>(ct);
            foreach (ModelEntry entry in body?.Models ?? [])
            {
                // A payload that isn't Google's shape: skip rather than dereference. See the same guard in
                // GeminiVertexLlmProvider for why a null here would surface as an opaque 500 on the
                // provider form instead of the 400 that screen is built to show.
                if (string.IsNullOrEmpty(entry.Name))
                {
                    continue;
                }

                // Skip embedding/tuning-only entries — offering them would just produce a 400 later.
                if (entry.SupportedGenerationMethods is { Count: > 0 } methods
                    && !methods.Contains("generateContent"))
                {
                    continue;
                }

                // "name" is the resource name ("models/gemini-x"); the id callers configure is the leaf.
                string id = entry.Name.StartsWith("models/", StringComparison.Ordinal)
                    ? entry.Name["models/".Length..]
                    : entry.Name;
                models.Add(new LlmModelInfo(id, entry.DisplayName));
            }

            pageToken = body?.NextPageToken;
            if (string.IsNullOrEmpty(pageToken))
            {
                break;
            }
        }

        return models;
    }

    private sealed record ModelListResponse(
        [property: JsonPropertyName("models")] IReadOnlyList<ModelEntry>? Models,
        [property: JsonPropertyName("nextPageToken")] string? NextPageToken);

    private sealed record ModelEntry(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("displayName")] string? DisplayName,
        [property: JsonPropertyName("supportedGenerationMethods")] IReadOnlyList<string>? SupportedGenerationMethods);
}
