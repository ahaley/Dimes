using System.Text.Json;
using System.Text.Json.Serialization;
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

        var (provider, connection) = await ResolveAgentProviderAsync(projectId, req.AgentActorId, ct);

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

    /// <summary>Freestyle Mode: decompose a freeform markdown brief into a list of editable change-order
    /// proposals. Stateless and recommend-only like <see cref="ChatAsync"/> — it never creates anything;
    /// the user edits the proposals and confirms a batch create. Blank markdown returns an empty list
    /// (not a 400) so the client's debounce can call freely while the user is still typing.</summary>
    public async Task<GenerateProposalsReplyDto> GenerateProposalsAsync(
        Guid projectId, GenerateProposalsRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Markdown))
        {
            return new GenerateProposalsReplyDto([]);
        }

        var (provider, connection) = await ResolveAgentProviderAsync(projectId, req.AgentActorId, ct);

        var completion = new LlmCompletionRequest(BuildProposalSystemPrompt(), req.Markdown, MaxTokens: 2048);
        var result = await provider.CompleteAsync(completion, connection, ct);
        return new GenerateProposalsReplyDto(ParseProposals(result.Text));
    }

    /// <summary>Shared agent → provider resolution: the actor must be an Agent with a configured LLM
    /// provider, and an adapter must be registered for that provider type. Returns the selected provider
    /// plus a connection carrying the resolved secret.</summary>
    private async Task<(ILlmProvider Provider, LlmConnection Connection)> ResolveAgentProviderAsync(
        Guid projectId, Guid agentActorId, CancellationToken ct)
    {
        var (actor, _) = await members.ResolveAsync(projectId, agentActorId, ct);
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

        // Re-validate at call time, not just at save time: a hostname that passed validation when the
        // provider was configured could now resolve to a cloud metadata endpoint (DNS rebinding). This
        // closes that TOCTOU window right before the outbound request is made.
        await ProviderUrlValidator.ValidateAsync(config.BaseUrl, ct);

        var connection = new LlmConnection(config.BaseUrl, config.Model, secrets.Resolve(config.ApiKeySecretRef));
        return (provider, connection);
    }

    private static string BuildProposalSystemPrompt() =>
        "You are a Capture Assistant in Dimes, a software change tracker. The user gives you a freeform " +
        "markdown brief. Decompose it into a list of discrete, well-scoped change requests. Respond with " +
        "ONLY a JSON array — no prose, no markdown code fences. Each element is an object with exactly " +
        "these keys: \"title\" (string, concise and imperative), \"description\" (string, one or two " +
        "sentences; may be empty), \"kind\" (one of \"Feature\", \"Problem\", \"ObservationDriven\"), " +
        "\"priority\" (one of \"None\", \"Low\", \"Medium\", \"High\", \"Critical\"). If the brief implies " +
        "no actionable changes, return []. Do not include any text before or after the array.";

    // The model is instructed to emit lenient string fields so a single bad enum value doesn't void the
    // whole array; titles/kinds/priorities are validated and defaulted during mapping.
    private sealed record RawProposal(string? Title, string? Description, string? Kind, string? Priority);

    private static readonly JsonSerializerOptions ProposalJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Tolerant parse of the model's reply into proposals. LLMs often wrap JSON in code fences or
    /// add stray prose despite instructions, so we strip fences, slice the outermost array, and on any
    /// failure return an empty list rather than throwing — the freestyle/debounce UX shows "no proposals"
    /// instead of erroring on every keystroke. Real provider/HTTP failures still surface upstream.</summary>
    internal static IReadOnlyList<CaptureProposalDto> ParseProposals(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var json = text.Trim();

        // Strip a leading/trailing markdown code fence (```json ... ``` or ``` ... ```), if present.
        if (json.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = json.IndexOf('\n');
            if (firstNewline >= 0)
            {
                json = json[(firstNewline + 1)..];
            }
            var closingFence = json.LastIndexOf("```", StringComparison.Ordinal);
            if (closingFence >= 0)
            {
                json = json[..closingFence];
            }
        }

        // Slice from the first '[' to the last ']' to tolerate any leftover surrounding prose.
        var start = json.IndexOf('[');
        var end = json.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            return [];
        }
        json = json[start..(end + 1)];

        List<RawProposal>? raw;
        try
        {
            raw = JsonSerializer.Deserialize<List<RawProposal>>(json, ProposalJsonOptions);
        }
        catch (JsonException)
        {
            return [];
        }
        if (raw is null)
        {
            return [];
        }

        var proposals = new List<CaptureProposalDto>(raw.Count);
        foreach (var p in raw)
        {
            if (string.IsNullOrWhiteSpace(p.Title))
            {
                continue; // a proposal with no title isn't actionable
            }
            var kind = Enum.TryParse<ChangeKind>(p.Kind, ignoreCase: true, out var k) ? k : ChangeKind.Feature;
            var priority = Enum.TryParse<Priority>(p.Priority, ignoreCase: true, out var pr) ? pr : Priority.None;
            var description = string.IsNullOrWhiteSpace(p.Description) ? null : p.Description.Trim();
            proposals.Add(new CaptureProposalDto(p.Title.Trim(), description, kind, priority));
        }
        return proposals;
    }
}
