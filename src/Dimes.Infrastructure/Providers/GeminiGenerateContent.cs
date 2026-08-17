using System.Text.Json.Serialization;
using Dimes.Domain.Providers;

namespace Dimes.Infrastructure.Providers;

/// <summary>The <c>generateContent</c> wire shape, shared by the two Gemini adapters. Google AI (AI
/// Studio) and Vertex AI differ in host, URL layout and authentication but speak the *same* request and
/// response body, so the payload lives here once and each adapter contributes only its endpoint + auth.
///
/// Field names are the canonical lowerCamelCase proto-JSON spellings, which both surfaces accept.</summary>
internal static class GeminiGenerateContent
{
    /// <summary>Translate a vendor-neutral request into a Gemini body.
    ///
    /// Two things differ from the OpenAI shape and are easy to get wrong: the system prompt is its own
    /// top-level <c>systemInstruction</c> field rather than a first message, and the assistant side of a
    /// replayed turn is spelled <c>model</c>, not <c>assistant</c>. Sending "assistant" makes multi-turn
    /// history (Capture Assist) fail rather than degrade, so the mapping is not optional.</summary>
    public static GeminiRequest BuildRequest(LlmCompletionRequest request)
    {
        List<GeminiContent> contents = [];
        foreach (LlmMessage message in request.History ?? [])
        {
            contents.Add(new GeminiContent(ToGeminiRole(message.Role), [new GeminiPart(message.Content)]));
        }
        contents.Add(new GeminiContent("user", [new GeminiPart(request.User)]));

        return new GeminiRequest(
            new GeminiContent(null, [new GeminiPart(request.System)]),
            contents,
            new GeminiGenerationConfig(request.MaxTokens));
    }

    private static string ToGeminiRole(string role) =>
        role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
        || role.Equals("model", StringComparison.OrdinalIgnoreCase)
            ? "model"
            : "user";

    /// <summary>Concatenate the text parts of the first candidate.
    ///
    /// An empty result is reported as a failure rather than stored as an empty agent comment — see
    /// <see cref="LlmHttp.RequireText"/>. Gemini names the cause in <c>finishReason</c> (including
    /// <c>MAX_TOKENS</c>) or, for a blocked prompt, <c>promptFeedback.blockReason</c>.</summary>
    public static string ExtractText(GeminiResponse? response)
    {
        GeminiCandidate? candidate = response?.Candidates?.FirstOrDefault();
        string text = string.Concat(
            (candidate?.Content?.Parts ?? []).Select(p => p.Text).Where(t => !string.IsNullOrEmpty(t)));

        string reason = candidate?.FinishReason
            ?? response?.PromptFeedback?.BlockReason
            ?? "no candidates were returned";
        return LlmHttp.RequireText(text, "Gemini", reason);
    }

    internal sealed record GeminiRequest(
        [property: JsonPropertyName("systemInstruction")] GeminiContent? SystemInstruction,
        [property: JsonPropertyName("contents")] IReadOnlyList<GeminiContent> Contents,
        [property: JsonPropertyName("generationConfig")] GeminiGenerationConfig GenerationConfig);

    // Role is null on a systemInstruction (it takes parts only) and omitted rather than sent as null.
    internal sealed record GeminiContent(
        [property: JsonPropertyName("role"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Role,
        [property: JsonPropertyName("parts")] IReadOnlyList<GeminiPart> Parts);

    internal sealed record GeminiPart(
        [property: JsonPropertyName("text")] string? Text);

    internal sealed record GeminiGenerationConfig(
        [property: JsonPropertyName("maxOutputTokens")] int MaxOutputTokens);

    internal sealed record GeminiResponse(
        [property: JsonPropertyName("candidates")] IReadOnlyList<GeminiCandidate>? Candidates,
        [property: JsonPropertyName("promptFeedback")] GeminiPromptFeedback? PromptFeedback);

    internal sealed record GeminiCandidate(
        [property: JsonPropertyName("content")] GeminiContent? Content,
        [property: JsonPropertyName("finishReason")] string? FinishReason);

    internal sealed record GeminiPromptFeedback(
        [property: JsonPropertyName("blockReason")] string? BlockReason);
}
