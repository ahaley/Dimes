namespace Dimes.Domain.Entities;

/// <summary>Configuration for an LLM endpoint behind the <c>LlmProvider</c> interface: Anthropic
/// (Claude), Gemini (Google AI or Vertex AI), or any OpenAI-compatible endpoint (OpenAI / local Ollama
/// / vLLM). The API key is referenced via the secret store, never stored here.</summary>
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

    /// <summary>Reference to the API key in the secret store — never the key itself. For
    /// <see cref="LlmProviderType.GeminiVertex"/> the referenced secret is a service-account
    /// <em>credentials JSON</em> (as with the Google Chat channel), not a bare key.</summary>
    public string? ApiKeySecretRef { get; set; }

    /// <summary>Provider-specific knobs as JSON — see <see cref="Providers.LlmProviderSettings"/>.
    /// Null for provider types that need none.</summary>
    public string? SettingsJson { get; set; }

    public bool Enabled { get; set; } = true;
}
