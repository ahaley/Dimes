using Dimes.Api.Contracts;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Domain.Providers;
using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Services;

/// <summary>Generates recommend-only commentary on a change via an Agent actor's configured LLM
/// endpoint. The output is stored as a Comment (AgentRecommendation); it never changes lifecycle
/// state — agents advise, humans decide (in pass-1).</summary>
public class CommentaryService(
    DimesDbContext db,
    IEnumerable<ILlmProvider> providers,
    ISecretResolver secrets,
    MembershipResolver members)
{
    public async Task<CommentDto> CommentOnChangeAsync(
        Guid changeId, Guid agentActorId, Guid callerActorId, bool callerIsSiteAdmin, CancellationToken ct = default)
    {
        var change = await db.ChangeRequests.FindAsync([changeId], ct)
            ?? throw new NotFoundException($"Change request '{changeId}' not found.");

        // Authorize the *caller*, not just the agent actor. Membership in the change's project is the
        // same bar as adding a human comment (AddCommentAsync). Without this, any authenticated user
        // could trigger an LLM completion on any change id — leaking its content to a non-member and
        // spending the configured provider's credits on demand.
        if (!callerIsSiteAdmin)
        {
            await members.ResolveAsync(change.ProjectId, callerActorId, ct); // throws ForbiddenException for non-members
        }

        var (actor, _) = await members.ResolveAsync(change.ProjectId, agentActorId, ct);
        if (actor.Type != ActorType.Agent)
        {
            throw new BadRequestException("Commentary requires an Agent actor.");
        }

        if (actor.LlmProviderConfigId is null)
        {
            throw new BadRequestException("The agent has no LLM provider configured.");
        }

        var config = await db.LlmProviderConfigs.FindAsync([actor.LlmProviderConfigId.Value], ct)
            ?? throw new NotFoundException("The agent's LLM provider config was not found.");

        var provider = providers.FirstOrDefault(p => p.Type == config.Type)
            ?? throw new BadRequestException($"No adapter is registered for provider type '{config.Type}'.");

        // Re-validate at call time, not just at save time: a hostname that passed validation when the
        // provider was configured could now resolve to a cloud metadata endpoint (DNS rebinding). This
        // closes that TOCTOU window right before the outbound request is made.
        await ProviderUrlValidator.ValidateAsync(config.BaseUrl, ct);

        var connection = new LlmConnection(config.BaseUrl, config.Model, secrets.Resolve(config.ApiKeySecretRef));
        var result = await provider.CompleteAsync(BuildPrompt(change), connection, ct);

        var comment = new Comment
        {
            ChangeRequestId = change.Id,
            AuthorActorId = actor.Id,
            Body = result.Text,
            Kind = CommentKind.AgentRecommendation,
        };
        db.Comments.Add(comment);
        await db.SaveChangesAsync(ct);
        return comment.ToDto();
    }

    private static LlmCompletionRequest BuildPrompt(ChangeRequest change)
    {
        const string system =
            "You are a software change-triage assistant. Comment concisely on the change request: " +
            "summarize it, flag risks or unknowns, and suggest a priority. Keep it brief. " +
            "This is advisory only — you do not approve or change anything.";
        var user =
            $"Title: {change.Title}\n" +
            $"Kind: {change.Kind}\n" +
            $"Status: {change.Status}\n\n" +
            $"Description:\n{change.Description ?? "(none)"}";
        return new LlmCompletionRequest(system, user);
    }
}
