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
    /// <summary>Assert the provider is in scope for <paramref name="projectId"/>: website-wide (null
    /// ProjectId) is in scope everywhere, a project-scoped one only in its own project.
    ///
    /// An Agent actor references a provider by id, and the picker only offers providers in scope for that
    /// project — but nothing re-checks an existing assignment afterwards. Moving a provider into a project
    /// (<see cref="ProjectService.MoveLlmProviderScopeAsync"/>) is refused while it would strand an agent, so
    /// this should be unreachable; it is asserted rather than assumed because the invariant is then enforced
    /// at the point of use instead of resting on the move guard staying correct.
    ///
    /// Kept separate from <see cref="ToConnectionAsync"/> because not every caller has a project scope to
    /// check against: model discovery probes a candidate configuration that isn't saved anywhere and belongs
    /// to no project, so it builds a connection without this assertion. Any path acting on behalf of an
    /// agent in a project MUST call this first.</summary>
    public static void RequireInScope(this LlmProviderConfig config, Guid projectId)
    {
        if (config.ProjectId is not null && config.ProjectId != projectId)
        {
            throw new BadRequestException(
                "The agent's LLM provider is scoped to a different project. Reassign the agent to a provider " +
                "available here.");
        }
    }

    /// <summary>Re-validate the base URL at call time — not just at save time — then resolve the secret.
    /// A hostname that passed validation when the provider was configured could now resolve to a cloud
    /// metadata endpoint (DNS rebinding); validating here closes that TOCTOU window immediately before the
    /// outbound request.
    ///
    /// Does not check project scope — see <see cref="RequireInScope"/>.</summary>
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
