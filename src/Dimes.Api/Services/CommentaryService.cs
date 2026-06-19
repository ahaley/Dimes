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
    public async Task<CommentDto> CommentOnChangeAsync(Guid changeId, Guid agentActorId, CancellationToken ct = default)
    {
        var change = await db.ChangeRequests.FindAsync([changeId], ct)
            ?? throw new NotFoundException($"Change request '{changeId}' not found.");

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
