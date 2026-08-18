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

    /// <summary>Override the reasoning mode the call site asked for. Null (the normal case) honours the
    /// request, which is what keeps reasoning stated rather than inherited.
    ///
    /// It exists because a call site chooses what Dimes wants and the operator chooses the model id, and
    /// only some models accept "off": Anthropic's Fable/Mythos family reasons unconditionally and rejects
    /// an explicit <c>thinking</c> with a 400, so such a config is uncallable until it can say
    /// <see cref="LlmReasoning.VendorDefault"/>. Refused at save time on the provider types whose adapters
    /// ignore reasoning entirely, so it can never sit here looking configured while doing nothing.</summary>
    [JsonPropertyName("reasoning")]
    public LlmReasoning? Reasoning { get; init; }

    /// <summary>Override the output token budget the call site asked for. Null uses the request's own.
    ///
    /// The companion to <see cref="Reasoning"/>, and vendor-neutral because the problem is: reasoning is
    /// drawn from the same budget as the answer, so a model that reasons regardless of what we ask will
    /// exhaust Dimes's small defaults (1024 for commentary, 2048 for proposals) before writing any text —
    /// which surfaces as a truncation error, not an answer. Raising the parameter was the standing advice
    /// in that error message with no way to act on it.</summary>
    [JsonPropertyName("maxTokens")]
    public int? MaxTokens { get; init; }

    /// <summary>Enums are written as strings here for the same reason they are in the database: the blob
    /// stays readable, and adding a member can't silently reinterpret rows already stored.</summary>
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

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
