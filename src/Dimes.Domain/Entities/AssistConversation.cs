namespace Dimes.Domain.Entities;

/// <summary>A persisted, two-way Capture Assist conversation between a requester and a HUMAN assistant.
/// (The AI-assistant path stays ephemeral and is not modeled here.) The request is also bubbled into
/// the assistant's observation inbox via <see cref="Observation"/>; when the requester captures the
/// resulting change it is linked through <see cref="ChangeRequest"/> for traceability.</summary>
public class AssistConversation : Entity
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = default!;

    /// <summary>The member shaping the idea.</summary>
    public Guid RequesterActorId { get; set; }
    public Actor Requester { get; set; } = default!;

    /// <summary>The human member being asked for help (Contributor+).</summary>
    public Guid AssistantActorId { get; set; }
    public Actor Assistant { get; set; } = default!;

    public AssistConversationStatus Status { get; set; } = AssistConversationStatus.AwaitingAssistant;

    /// <summary>Working title snapshot (optional).</summary>
    public string? Title { get; set; }

    /// <summary>Composed rough/title/description snapshot, shown to the assistant as context.</summary>
    public string? Draft { get; set; }

    /// <summary>The inbox bubble-up: the observation that surfaces this request to the assistant.</summary>
    public Guid? ObservationId { get; set; }
    public Observation? Observation { get; set; }

    /// <summary>Set when the requester captures the change this conversation helped shape.</summary>
    public Guid? ChangeRequestId { get; set; }
    public ChangeRequest? ChangeRequest { get; set; }

    /// <summary>Last-activity timestamp for ordering (created/each message/close bumps it).</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<AssistMessage> Messages { get; set; } = new List<AssistMessage>();
}
