using System.Text.Json;

namespace Dimes.Infrastructure.Providers;

/// <summary>Shared HTTP-response handling for the LLM adapters: surface the vendor's own error text, and
/// refuse to return an empty completion.
///
/// Consolidated because all three adapters need the identical behaviour and the *wording* is the useful
/// part — an operator reading "raise MaxTokens" is the difference between a fixable config and a mystery.
/// Keeping it in one place also stops the guidance drifting apart as adapters are added.</summary>
internal static class LlmHttp
{
    /// <summary>Throw with the vendor's own error text on a non-success status.
    ///
    /// <see cref="HttpResponseMessage.EnsureSuccessStatusCode"/> discards the body, which is where every
    /// actionable detail lives: "API key not valid", "model not found", or a parameter the model doesn't
    /// accept — for example a <c>thinking</c> mode that a particular model rejects. A bare status code
    /// leaves the operator guessing at which of those it was.</summary>
    public static async Task EnsureSuccessAsync(
        HttpResponseMessage response, string provider, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var detail = await ReadErrorAsync(response.Content, ct);
        throw new HttpRequestException(
            $"{provider} request failed ({(int)response.StatusCode} {response.ReasonPhrase}): {detail}");
    }

    /// <summary>Read an error body for an exception message. Most vendors nest the useful text at
    /// <c>error.message</c>; falling back to the (trimmed) raw body keeps this honest when the shape is
    /// something else entirely, such as an HTML error page from a proxy.</summary>
    public static async Task<string> ReadErrorAsync(HttpContent content, CancellationToken ct)
    {
        var body = await content.ReadAsStringAsync(ct);
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error)
                && error.TryGetProperty("message", out var message)
                && message.GetString() is string text)
            {
                return text;
            }
        }
        catch (JsonException)
        {
            // Not JSON — fall through to the raw body.
        }

        return body.Length <= 500 ? body : body[..500];
    }

    /// <summary>Return the completion text, or throw naming why there wasn't any.
    ///
    /// Adapters used to coalesce a missing completion to <see cref="string.Empty"/>, which stored a blank
    /// <c>AgentRecommendation</c> comment — indistinguishable from the model having nothing to say, and
    /// silent. The likely causes are all actionable once named: a safety block, or a response truncated at
    /// the token limit. Truncation gets an explicit hint because reasoning tokens come out of the same
    /// budget as the answer, so a reasoning-enabled call on a small budget can be cut off before writing
    /// any text at all.</summary>
    public static string RequireText(string? text, string provider, string? reason)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var described = string.IsNullOrWhiteSpace(reason) ? "not reported" : reason;
        var hint = LooksLikeTruncation(reason)
            ? " The response hit the token limit before producing text — raise MaxTokens, or disable " +
              "reasoning for this call (reasoning is drawn from the same budget as the answer)."
            : string.Empty;

        throw new HttpRequestException($"{provider} returned no text content (reason: {described}).{hint}");
    }

    /// <summary>Every vendor spells "ran out of output budget" differently — Anthropic <c>max_tokens</c>,
    /// Gemini <c>MAX_TOKENS</c>, OpenAI <c>length</c>. The first two differ only in case, so the one
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> match covers both; a separate <c>MAX_TOKENS</c>
    /// clause used to sit alongside it and was unreachable. Don't add it back — it reads as though it is
    /// what covers Gemini, which invites "fixing" the wrong branch later.
    ///
    /// Matched loosely on purpose: a missed match only costs the extra hint, while the reason itself is
    /// always reported.</summary>
    private static bool LooksLikeTruncation(string? reason) =>
        reason is not null
        && (reason.Contains("max_tokens", StringComparison.OrdinalIgnoreCase)
            || reason.Equals("length", StringComparison.OrdinalIgnoreCase));
}
