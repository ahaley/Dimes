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
            // No adapter is in this state today — every registered type implements the catalog. It stays
            // because the capability is optional by design (a minimal local runner may expose no listing
            // route), and because the alternative is a NullReferenceException in that case.
            throw new BadRequestException(
                $"Dimes cannot list models for {req.Type} endpoints — enter the model id directly.");
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
