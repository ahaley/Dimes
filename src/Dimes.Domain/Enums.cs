namespace Dimes.Domain;

/// <summary>An actor is either a human user or an AI agent endpoint.</summary>
public enum ActorType
{
    Human,
    Agent,
}

/// <summary>Per-project role. Maintainer is the elevated whitelist authority.</summary>
public enum MemberRole
{
    // Assistant is the lowest authority (ordinal 0) and agent-only: a conversational capture
    // helper. It sits below Reporter so it can never satisfy any lifecycle role guard
    // (LifecycleService compares roles with '<'), keeping such agents strictly recommend-only.
    Assistant,
    Reporter,
    Contributor,
    Maintainer,
}

/// <summary>Where an observation originated. <see cref="Internal"/> is for signals Dimes raises about
/// itself (e.g. a Capture Assist request bubbled to a human assistant), not an external capture source.</summary>
public enum ObservationSourceType
{
    Sdk,
    Seq,
    Internal,
}

/// <summary>The capture-spectrum category of an observation.</summary>
public enum ObservationKind
{
    ExplicitFeedback,
    SolicitedFeedback,
    BehavioralFriction,
    TechnicalError,
    /// <summary>A Capture Assist request directed at a human assistant (see <see cref="Entities.AssistConversation"/>).</summary>
    AssistRequest,
}

/// <summary>Lifecycle of a human Capture Assist conversation. It awaits whichever side must act next,
/// then closes (typically when the requester captures the change).</summary>
public enum AssistConversationStatus
{
    AwaitingAssistant,
    AwaitingRequester,
    Closed,
}

/// <summary>Which side of a Capture Assist conversation authored a message.</summary>
public enum AssistMessageSender
{
    Requester,
    Assistant,
}

/// <summary>Observation inbox lifecycle.</summary>
public enum ObservationStatus
{
    New,
    Clustered,
    Promoted,
    Dismissed,
}

/// <summary>What kind of change a request represents.</summary>
public enum ChangeKind
{
    Problem,
    Feature,
    ObservationDriven,
    /// <summary>A composite change that groups other change requests as composed children (see
    /// <see cref="Entities.ChangeRequest.ParentChangeRequestId"/>). Flows through the same lifecycle.</summary>
    Epic,
}

/// <summary>The change-request lifecycle (the spine). Order matters for the happy path.</summary>
public enum ChangeStatus
{
    Captured,
    Triaged,
    Approved,
    InDevelopment,
    InReview,
    Done,
    Rejected,
    Duplicate,
}

public enum Priority
{
    None,
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>Distinguishes human comments from recommend-only agent commentary.</summary>
public enum CommentKind
{
    Human,
    AgentRecommendation,
}

public enum LlmProviderType
{
    Anthropic,
    OpenAICompatible,
}

public enum ScmProviderType
{
    GitHub,
}

/// <summary>Which entity an <see cref="Entities.AuditEvent"/> describes (polymorphic, not an FK).</summary>
public enum AuditEntityType
{
    ChangeRequest,
    Observation,
}

/// <summary>Which feature an editable <see cref="Entities.SystemInstruction"/> drives. Today only the
/// In-Development export "work order" preamble; the discriminator lets future instruction blocks reuse
/// the same per-project table.</summary>
public enum SystemInstructionKind
{
    ExportWorkOrder,
}
