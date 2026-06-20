using Dimes.Api.Contracts;
using Dimes.Domain;
using Dimes.Domain.Providers;
using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Services;

/// <summary>Conversational helper for Capture Assist Mode: drives an Agent actor's configured LLM
/// through a multi-turn dialog that helps a user shape a loose idea into a well-formed change
/// request. Stateless — the conversation lives in the client and is replayed on each call; nothing
/// is persisted here. Like commentary, it is advisory only: it never creates or mutates state. The
/// user confirms a title/description and creates the change through the normal create endpoint.</summary>
public class CaptureAssistService(
    DimesDbContext db,
    IEnumerable<ILlmProvider> providers,
    ISecretResolver secrets,
    MembershipResolver members)
{
    public async Task<CaptureAssistReplyDto> ChatAsync(
        Guid projectId, CaptureAssistChatRequest req, CancellationToken ct = default)
    {
        if (req.Messages.Count == 0 || !string.Equals(req.Messages[^1].Role, "user", StringComparison.OrdinalIgnoreCase))
        {
            throw new BadRequestException("The conversation must end with a user message.");
        }

        var (actor, _) = await members.ResolveAsync(projectId, req.AgentActorId, ct);
        if (actor.Type != ActorType.Agent)
        {
            throw new BadRequestException("Capture Assist requires an Agent actor.");
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

        // Replay everything before the latest user turn as history; the last user turn is the prompt.
        var history = req.Messages
            .Take(req.Messages.Count - 1)
            .Select(m => new LlmMessage(m.Role.ToLowerInvariant(), m.Content))
            .ToList();
        var completion = new LlmCompletionRequest(
            BuildSystemPrompt(req.Draft), req.Messages[^1].Content, MaxTokens: 1024, History: history);

        var result = await provider.CompleteAsync(completion, connection, ct);
        return new CaptureAssistReplyDto(result.Text);
    }

    private static string BuildSystemPrompt(string? draft)
    {
        const string baseInstructions =
            "You are a Capture Assistant in Dimes, a software change tracker. Help the user develop a " +
            "loose idea into a well-formed change request. Ask focused clarifying questions, help refine " +
            "scope and acceptance, and when the idea is clear enough propose a concise title and a clear " +
            "description the user can save. Keep replies brief and conversational. You are advisory only — " +
            "you never create or change anything; the user confirms the title and description and creates " +
            "the change request themselves.";
        return string.IsNullOrWhiteSpace(draft)
            ? baseInstructions
            : $"{baseInstructions}\n\nThe user's current rough draft:\n{draft}";
    }
}
