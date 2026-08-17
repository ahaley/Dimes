using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dimes.Domain.Providers;

/// <summary>Provider-specific configuration that only some adapters need, persisted as one JSON column
/// (<c>LlmProviderConfig.SettingsJson</c>) rather than a widening set of mostly-null typed columns.
///
/// This follows the existing JSON-column convention (<c>Observation.Payload</c>, <c>ContextMetadata</c>,
/// <c>NotificationChannelConfig.EventsJson</c>) and buys a concrete thing: the next adapter that needs a
/// knob adds a property here and needs no migration on either provider's migration set. It stays a
/// strongly-typed record — nothing reads it by string key.</summary>
public sealed record LlmProviderSettings
{
    /// <summary>Vertex only: the GCP project id that owns the Vertex AI endpoint.</summary>
    [JsonPropertyName("gcpProject")]
    public string? GcpProject { get; init; }

    /// <summary>Vertex only: the region serving the model (e.g. <c>us-central1</c>, <c>europe-west4</c>),
    /// or <c>global</c>. This is the data-residency control, which is most of why Vertex exists as a
    /// separate type — it selects the regional host as well as the URL path.</summary>
    [JsonPropertyName("gcpLocation")]
    public string? GcpLocation { get; init; }

    /// <summary>Vertex only: mint tokens from Application Default Credentials instead of a stored
    /// credentials JSON. On GKE / Cloud Run with Workload Identity this means no secret exists to leak,
    /// which is strictly safer than any key we could store a reference to — so it is the documented
    /// default and the reason <c>ApiKeySecretRef</c> stays optional for Vertex.</summary>
    [JsonPropertyName("useApplicationDefaultCredentials")]
    public bool UseApplicationDefaultCredentials { get; init; }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Parse a stored settings blob. A null/blank/corrupt blob yields default settings rather
    /// than throwing: a config row must stay readable and editable in the UI even if its settings JSON
    /// was hand-edited badly, otherwise the operator can't fix it. Missing required values are caught by
    /// the save-time validation instead.</summary>
    public static LlmProviderSettings Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new LlmProviderSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<LlmProviderSettings>(json, Json) ?? new LlmProviderSettings();
        }
        catch (JsonException)
        {
            return new LlmProviderSettings();
        }
    }

    /// <summary>Serialize for storage, collapsing "nothing set" to null so unaffected provider types
    /// keep an empty column instead of a noise blob.</summary>
    public string? ToJson() =>
        this == new LlmProviderSettings() ? null : JsonSerializer.Serialize(this, Json);
}
