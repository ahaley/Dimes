using System.Text.Json;
using Dimes.Api.Contracts;
using Dimes.Api.Realtime;
using Dimes.Domain;
using Dimes.Domain.Entities;
using Dimes.Domain.Lifecycle;
using Dimes.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Dimes.Api.Services;

/// <summary>Drives the persisted, two-way Capture Assist conversation with a HUMAN assistant. The
/// requester's messages are bubbled into the assistant's observation inbox (the same mechanism that
/// surfaces latent signals); the assistant replies and the thread flows back to the requester. This is
/// human-to-human messaging — it never changes any change-request state. The only lifecycle touch is a
/// legal New → Dismissed transition on the bubble-up observation, performed through
/// <see cref="LifecycleService"/>.</summary>
public class AssistConversationService(
    DimesDbContext db, MembershipResolver members, LifecycleService lifecycle, IBoardNotifier notifier)
{
    private const string AssistNotificationKind = "assist";

    public async Task<AssistConversationDto> StartAsync(
        Guid projectId, Guid requesterActorId, StartAssistConversationRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Message))
        {
            throw new BadRequestException("A message is required to start a conversation.");
        }

        var (requester, _) = await members.ResolveAsync(projectId, requesterActorId, ct);
        var (assistant, assistantRole) = await members.ResolveAsync(projectId, req.AssistantActorId, ct);

        if (assistant.Id == requester.Id)
        {
            throw new BadRequestException("You can't ask yourself for help.");
        }
        if (assistant.Type != ActorType.Human)
        {
            throw new BadRequestException(
                "A persisted Capture Assist conversation requires a human assistant. Use the AI assistant chat for agents.");
        }
        if (assistantRole < MemberRole.Contributor)
        {
            throw new BadRequestException("The human assistant must be a Contributor or Maintainer of this project.");
        }

        var conversation = new AssistConversation
        {
            ProjectId = projectId,
            RequesterActorId = requester.Id,
            AssistantActorId = assistant.Id,
            Status = AssistConversationStatus.AwaitingAssistant,
            Title = string.IsNullOrWhiteSpace(req.Title) ? null : req.Title.Trim(),
            Draft = req.Draft,
        };
        conversation.Messages.Add(new AssistMessage
        {
            AuthorActorId = requester.Id,
            Sender = AssistMessageSender.Requester,
            Body = req.Message.Trim(),
        });

        // Bubble the request into the assistant's inbox via the existing observation mechanism.
        conversation.Observation =
            await BuildBubbleUpAsync(projectId, conversation.Id, assistant.Id, requester.DisplayName, req.Message, ct);

        db.AssistConversations.Add(conversation);
        await db.SaveChangesAsync(ct);
        await notifier.ChangedAsync(projectId, conversation.Id, AssistNotificationKind, ct);
        return await GetDtoAsync(conversation.Id, ct);
    }

    public async Task<AssistConversationDto> PostMessageAsync(
        Guid projectId, Guid conversationId, Guid currentActorId, PostAssistMessageRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Body))
        {
            throw new BadRequestException("Message body is required.");
        }

        var conversation = await db.AssistConversations
            .Include(c => c.Requester)
            .Include(c => c.Observation)
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.ProjectId == projectId, ct)
            ?? throw new NotFoundException($"Conversation '{conversationId}' not found.");

        if (conversation.Status == AssistConversationStatus.Closed)
        {
            throw new BadRequestException("This conversation is closed.");
        }

        var sender = ResolveSender(conversation, currentActorId);
        db.AssistMessages.Add(new AssistMessage
        {
            ConversationId = conversation.Id,
            AuthorActorId = currentActorId,
            Sender = sender,
            Body = req.Body.Trim(),
        });

        if (sender == AssistMessageSender.Assistant)
        {
            conversation.Status = AssistConversationStatus.AwaitingRequester;
            // The assistant has engaged — clear the inbox request through the lifecycle engine.
            if (conversation.Observation is { Status: ObservationStatus.New } obs)
            {
                var (actor, role) = await members.ResolveAsync(projectId, currentActorId, ct);
                db.AuditEvents.Add(lifecycle.TransitionObservation(obs, ObservationStatus.Dismissed, actor, role, "Assist answered"));
            }
        }
        else
        {
            conversation.Status = AssistConversationStatus.AwaitingAssistant;
            // Re-surface in the assistant's inbox. Reuse the open observation if one is still pending;
            // otherwise (the prior request was answered/dismissed) raise a fresh one.
            if (conversation.Observation is { Status: ObservationStatus.New } open)
            {
                open.OccurrenceCount++;
                open.LastSeen = DateTimeOffset.UtcNow;
                open.Payload = BuildPayload(conversation.Id, conversation.Requester.DisplayName, req.Body);
            }
            else
            {
                conversation.Observation = await BuildBubbleUpAsync(
                    projectId, conversation.Id, conversation.AssistantActorId, conversation.Requester.DisplayName, req.Body, ct);
            }
        }

        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await notifier.ChangedAsync(projectId, conversation.Id, AssistNotificationKind, ct);
        return await GetDtoAsync(conversation.Id, ct);
    }

    public async Task<AssistConversationDto> GetAsync(
        Guid projectId, Guid conversationId, Guid currentActorId, bool isSiteAdmin, CancellationToken ct = default)
    {
        var conversation = await db.AssistConversations
            .Include(c => c.Requester)
            .Include(c => c.Assistant)
            .Include(c => c.Messages)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.ProjectId == projectId, ct)
            ?? throw new NotFoundException($"Conversation '{conversationId}' not found.");

        if (!isSiteAdmin && currentActorId != conversation.RequesterActorId && currentActorId != conversation.AssistantActorId)
        {
            throw new ForbiddenException("You are not a participant in this conversation.");
        }
        return conversation.ToDto();
    }

    public async Task<IReadOnlyList<AssistConversationSummaryDto>> ListAsync(
        Guid projectId, Guid currentActorId, string role, AssistConversationStatus? status, CancellationToken ct = default)
    {
        var query = db.AssistConversations.Where(c => c.ProjectId == projectId);
        query = string.Equals(role, "requester", StringComparison.OrdinalIgnoreCase)
            ? query.Where(c => c.RequesterActorId == currentActorId)
            : query.Where(c => c.AssistantActorId == currentActorId);
        if (status is not null)
        {
            query = query.Where(c => c.Status == status);
        }

        return await query
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new AssistConversationSummaryDto(
                c.Id, c.ProjectId,
                c.RequesterActorId, c.Requester.DisplayName,
                c.AssistantActorId, c.Assistant.DisplayName,
                c.Status, c.Title,
                c.Messages.OrderByDescending(m => m.CreatedAt).Select(m => m.Body).FirstOrDefault(),
                c.Messages.Count,
                c.UpdatedAt))
            .ToListAsync(ct);
    }

    public async Task<AssistConversationDto> CloseAsync(
        Guid projectId, Guid conversationId, Guid currentActorId, Guid? changeRequestId, CancellationToken ct = default)
    {
        var conversation = await db.AssistConversations
            .Include(c => c.Observation)
            .FirstOrDefaultAsync(c => c.Id == conversationId && c.ProjectId == projectId, ct)
            ?? throw new NotFoundException($"Conversation '{conversationId}' not found.");

        if (currentActorId != conversation.RequesterActorId && currentActorId != conversation.AssistantActorId)
        {
            throw new ForbiddenException("Only a participant can close this conversation.");
        }

        if (changeRequestId is Guid crId)
        {
            if (!await db.ChangeRequests.AnyAsync(c => c.Id == crId && c.ProjectId == projectId, ct))
            {
                throw new NotFoundException($"Change request '{crId}' not found in project '{projectId}'.");
            }
            conversation.ChangeRequestId = crId;
        }

        conversation.Status = AssistConversationStatus.Closed;

        // Clear any still-open inbox request. If the closer can't satisfy the Contributor guard (a
        // Reporter requester), leave it — the assistant can still dismiss it from their inbox.
        if (conversation.Observation is { Status: ObservationStatus.New } obs)
        {
            var (actor, role) = await members.ResolveAsync(projectId, currentActorId, ct);
            if (role >= MemberRole.Contributor)
            {
                db.AuditEvents.Add(lifecycle.TransitionObservation(obs, ObservationStatus.Dismissed, actor, role, "Assist closed"));
            }
        }

        conversation.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await notifier.ChangedAsync(projectId, conversation.Id, AssistNotificationKind, ct);
        return await GetDtoAsync(conversation.Id, ct);
    }

    private static AssistMessageSender ResolveSender(AssistConversation c, Guid actorId)
    {
        if (actorId == c.RequesterActorId)
        {
            return AssistMessageSender.Requester;
        }
        if (actorId == c.AssistantActorId)
        {
            return AssistMessageSender.Assistant;
        }
        throw new ForbiddenException("Only the requester or the assistant can post to this conversation.");
    }

    /// <summary>Build (but don't save) the bubble-up observation that surfaces a request to the assistant's
    /// inbox, auto-provisioning the project's internal source on first use.</summary>
    private async Task<Observation> BuildBubbleUpAsync(
        Guid projectId, Guid conversationId, Guid assistantId, string requesterName, string message, CancellationToken ct)
    {
        var source = await GetOrCreateInternalSourceAsync(projectId, ct);
        return new Observation
        {
            ProjectId = projectId,
            SourceId = source.Id,
            Kind = ObservationKind.AssistRequest,
            Status = ObservationStatus.New,
            TargetActorId = assistantId,
            Payload = BuildPayload(conversationId, requesterName, message),
        };
    }

    private async Task<ObservationSource> GetOrCreateInternalSourceAsync(Guid projectId, CancellationToken ct)
    {
        var source = await db.ObservationSources
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.Type == ObservationSourceType.Internal, ct);
        if (source is null)
        {
            source = new ObservationSource
            {
                ProjectId = projectId,
                Type = ObservationSourceType.Internal,
                Name = "Capture Assist",
            };
            db.ObservationSources.Add(source);
        }
        return source;
    }

    private static string BuildPayload(Guid conversationId, string requesterName, string message)
    {
        var preview = message.Trim();
        if (preview.Length > 200)
        {
            preview = preview[..200] + "…";
        }
        return JsonSerializer.Serialize(new { conversationId, requesterName, preview });
    }

    private async Task<AssistConversationDto> GetDtoAsync(Guid conversationId, CancellationToken ct)
    {
        var conversation = await db.AssistConversations
            .Include(c => c.Requester)
            .Include(c => c.Assistant)
            .Include(c => c.Messages)
            .AsNoTracking()
            .FirstAsync(c => c.Id == conversationId, ct);
        return conversation.ToDto();
    }
}
