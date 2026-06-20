namespace Dimes.Domain.Providers;

/// <summary>Per-call connection details resolved from an <c>LlmProviderConfig</c> (+ secret store).
/// Kept separate from the config entity so providers stay free of persistence concerns.</summary>
public sealed record LlmConnection(string? BaseUrl, string Model, string? ApiKey);

/// <summary>One turn in a multi-turn exchange. <see cref="Role"/> is "user" or "assistant".</summary>
public sealed record LlmMessage(string Role, string Content);

/// <summary>A recommend-only completion request: a system instruction plus the user content.
/// <see cref="History"/> holds prior conversation turns (oldest first); adapters replay them ahead
/// of the final <see cref="User"/> turn so the model has context. Null/empty means a one-shot call.</summary>
public sealed record LlmCompletionRequest(
    string System, string User, int MaxTokens = 1024, IReadOnlyList<LlmMessage>? History = null);

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
