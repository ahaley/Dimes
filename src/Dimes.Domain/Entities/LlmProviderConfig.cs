namespace Dimes.Domain.Entities;

/// <summary>Configuration for an LLM endpoint behind the <c>LlmProvider</c> interface.
/// Anthropic (Claude) or any OpenAI-compatible endpoint (OpenAI / local Ollama / vLLM).
/// The API key is referenced via the secret store and encrypted at rest.</summary>
public class LlmProviderConfig : Entity
{
    /// <summary>Null = global (applies across projects).</summary>
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }

    public LlmProviderType Type { get; set; }
    public required string Name { get; set; }

    /// <summary>Base URL for OpenAI-compatible / local endpoints. Null uses the provider default.</summary>
    public string? BaseUrl { get; set; }
    public required string Model { get; set; }

    /// <summary>Reference to the API key in the secret store — never the key itself.</summary>
    public string? ApiKeySecretRef { get; set; }

    public bool Enabled { get; set; } = true;
}
