namespace Dimes.Domain.Entities;

/// <summary>A single turn in a human Capture Assist <see cref="AssistConversation"/>.</summary>
public class AssistMessage : Entity
{
    public Guid ConversationId { get; set; }
    public AssistConversation Conversation { get; set; } = default!;

    public Guid AuthorActorId { get; set; }
    public Actor Author { get; set; } = default!;

    /// <summary>Which side authored this turn — drives the chat-pane alignment.</summary>
    public AssistMessageSender Sender { get; set; }

    public required string Body { get; set; }
}
