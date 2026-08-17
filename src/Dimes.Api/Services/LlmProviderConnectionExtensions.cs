using Dimes.Domain.Entities;
using Dimes.Domain.Providers;

namespace Dimes.Api.Services;

/// <summary>Turns a stored <see cref="LlmProviderConfig"/> into the per-call
/// <see cref="LlmConnection"/> an adapter needs.
///
/// It exists so the SSRF re-validation and the connection build cannot drift apart. Both call sites
/// (agent commentary and Capture Assist) previously repeated the pair by hand, and a third caller — model
/// discovery — would have repeated it again; a caller that built the connection but forgot the validation
/// would silently reopen the hole the validator closes.</summary>
public static class LlmProviderConnectionExtensions
{
    /// <summary>Re-validate the base URL at call time — not just at save time — then resolve the secret.
    /// A hostname that passed validation when the provider was configured could now resolve to a cloud
    /// metadata endpoint (DNS rebinding); validating here closes that TOCTOU window immediately before the
    /// outbound request.</summary>
    public static async Task<LlmConnection> ToConnectionAsync(
        this LlmProviderConfig config, ISecretResolver secrets, CancellationToken ct = default)
    {
        await ProviderUrlValidator.ValidateAsync(config.Type, config.BaseUrl, ct);
        return new LlmConnection(
            config.BaseUrl,
            config.Model,
            secrets.Resolve(config.ApiKeySecretRef),
            LlmProviderSettings.Parse(config.SettingsJson));
    }
}
