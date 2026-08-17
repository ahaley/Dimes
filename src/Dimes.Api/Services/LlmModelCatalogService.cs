using Dimes.Api.Contracts;
using Dimes.Domain.Entities;
using Dimes.Domain.Providers;

namespace Dimes.Api.Services;

/// <summary>Lists the models a provider configuration can actually reach.
///
/// The point is to keep model ids out of the codebase. <c>LlmProviderConfig.Model</c> is free text and
/// vendors ship and rename models constantly, so a hardcoded dropdown would be stale within weeks and a
/// wrong id only shows up as a 400 at first use. Asking the endpoint instead means a newly released model
/// is selectable with no code change.
///
/// It takes the values from the config form rather than a saved id so the same call serves both the add
/// and edit forms — but it resolves the credential from the secret store by reference, exactly as a real
/// call does, so this never becomes a way to smuggle a key through the API.</summary>
public class LlmModelCatalogService(IEnumerable<ILlmProvider> providers, ISecretResolver secrets)
{
    /// <summary>Callers MUST gate this with the same authority as creating a provider config — it triggers
    /// an authenticated outbound request using a stored credential.</summary>
    public async Task<IReadOnlyList<LlmModelDto>> ListModelsAsync(
        ListLlmModelsRequest req, CancellationToken ct = default)
    {
        var provider = providers.FirstOrDefault(p => p.Type == req.Type)
            ?? throw new BadRequestException($"No adapter is registered for provider type '{req.Type}'.");

        if (provider is not ILlmModelCatalog catalog)
        {
            // Vertex AI is the case today: publisher models are not enumerable the same way, and Dimes
            // will not guess. The message points at the workaround that actually works, because Vertex
            // serves the same Gemini model ids as the Google AI API.
            throw new BadRequestException(
                $"{req.Type} endpoints do not support model discovery — enter the model id directly. " +
                "Vertex AI serves the same Gemini model ids as a Gemini (Google AI) provider, so you can " +
                "discover the id there and paste it here.");
        }

        // An unsaved stand-in, so discovery goes through the same validate-then-resolve path as a real
        // call (including the per-type base-URL policy). Model is unused by a listing.
        var probe = new LlmProviderConfig
        {
            Type = req.Type,
            Name = string.Empty,
            BaseUrl = req.BaseUrl,
            Model = string.Empty,
            ApiKeySecretRef = req.ApiKeySecretRef,
            SettingsJson = req.Settings.ToSettingsJson(),
        };
        try
        {
            var connection = await probe.ToConnectionAsync(secrets, ct);
            var models = await catalog.ListModelsAsync(connection, ct);
            return models
                .OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
                .Select(m => new LlmModelDto(m.Id, m.DisplayName))
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            // A bad key, an unreadable secret file, an endpoint with no /models route, or an unreachable
            // host is a configuration problem, not a server fault — report it as a 400 the form can
            // display. This is the interactive surface where such a mistake is made, so the message
            // reaching the operator matters more here than anywhere else.
            throw new BadRequestException($"Could not list models from this endpoint: {ex.Message}");
        }
    }
}
