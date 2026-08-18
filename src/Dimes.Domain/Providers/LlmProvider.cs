namespace Dimes.Domain.Providers;

/// <summary>Per-call connection details resolved from an <c>LlmProviderConfig</c> (+ secret store).
/// Kept separate from the config entity so providers stay free of persistence concerns.
///
/// <paramref name="ApiKey"/> carries whatever credential the adapter's auth model needs — a bare key for
/// Anthropic/OpenAI/Gemini, or a service-account credentials JSON for Vertex. <paramref name="Settings"/>
/// is null when the provider type needs no extra configuration.</summary>
public sealed record LlmConnection(
    string? BaseUrl, string Model, string? ApiKey, LlmProviderSettings? Settings = null);

/// <summary>One turn in a multi-turn exchange. <see cref="Role"/> is "user" or "assistant" — the
/// vendor-neutral spelling. Adapters translate as needed (Gemini calls the assistant side "model").</summary>
public sealed record LlmMessage(string Role, string Content);

/// <summary>How much internal reasoning to ask of the model for one call. Vendor-neutral: adapters
/// translate it to their own parameter, or ignore it where the endpoint has no equivalent.
///
/// This is stated explicitly rather than left to the vendor default because **the vendor default is not
/// stable across models**. Omitting Anthropic's <c>thinking</c> field means "no thinking" on Sonnet 4.6
/// but "adaptive thinking" on Sonnet 5 — and a request's token budget covers reasoning *and* the answer.
/// So a config whose only change was its model id could start spending most of its budget on reasoning
/// and return truncated or empty text. Saying what we want keeps behaviour fixed as models move.</summary>
public enum LlmReasoning
{
    /// <summary>No extended reasoning — the whole token budget goes to the answer. The right default for
    /// short recommend-only output, and what every Dimes call has effectively been getting so far.</summary>
    Disabled,

    /// <summary>Let the model decide how much to reason. Budget for it: reasoning is drawn from the same
    /// <see cref="LlmCompletionRequest.MaxTokens"/> as the answer, so enabling this on the small default
    /// budget can truncate the call before it produces any text.</summary>
    Adaptive,
}

/// <summary>A recommend-only completion request: a system instruction plus the user content.
/// <see cref="History"/> holds prior conversation turns (oldest first); adapters replay them ahead
/// of the final <see cref="User"/> turn so the model has context. Null/empty means a one-shot call.</summary>
public sealed record LlmCompletionRequest(
    string System, string User, int MaxTokens = 1024, IReadOnlyList<LlmMessage>? History = null,
    LlmReasoning Reasoning = LlmReasoning.Disabled);

public sealed record LlmCompletionResult(string Text);

/// <summary>The thin LLM seam. Concrete adapters (Claude, OpenAI-compatible/local) implement this;
/// callers select one by <see cref="Type"/>. Pass-1 use is recommend-only — providers never mutate
/// domain state.</summary>
public interface ILlmProvider
{
    LlmProviderType Type { get; }

    Task<LlmCompletionResult> CompleteAsync(
        LlmCompletionRequest request, LlmConnection connection, CancellationToken ct = default);
}

/// <summary>One model an endpoint reports. <see cref="DisplayName"/> is whatever that vendor offers as a
/// human label, or null where it offers none — Vertex's catalog has no display name, so its adapter puts
/// the launch stage there instead.</summary>
public sealed record LlmModelInfo(string Id, string? DisplayName = null);

/// <summary>Optional capability: enumerate the models available to a connection, so the config UI can
/// offer real ids instead of a hardcoded list that goes stale on every vendor release.
///
/// Deliberately a *separate* interface rather than a member of <see cref="ILlmProvider"/>, for two
/// reasons. Listing is not universal — a minimal local runner may implement no listing route at all — and
/// callers must be able to tell "this endpoint has no catalog" from "the catalog call failed". And the
/// vendors disagree wildly on shape: a Google AI models list, an Anthropic paged list, and a Vertex
/// *publisher catalog* on a different API version share nothing but the idea, so the normalising seam has
/// to live somewhere. Callers test with <c>provider is ILlmModelCatalog</c>.
///
/// What a listing means also varies, and callers should not over-promise: most are scoped to the
/// credential and so report what it can reach, but Vertex's is a published catalog and a listed id may
/// still be unavailable to a given project or region.</summary>
public interface ILlmModelCatalog
{
    Task<IReadOnlyList<LlmModelInfo>> ListModelsAsync(
        LlmConnection connection, CancellationToken ct = default);
}
