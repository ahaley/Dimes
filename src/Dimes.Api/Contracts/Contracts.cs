using Dimes.Domain;

namespace Dimes.Api.Contracts;

// NOTE: pass-1 has no authentication yet, so actions that require an actor accept an ActorId in the
// request. A real identity/auth layer is a future slice; the ActorId here is the stand-in.

// ----- Projects & members -----
public record CreateProjectRequest(string Name, string? Description);
public record ProjectDto(Guid Id, string Name, string? Description, DateTimeOffset CreatedAt);

public record AddMemberRequest(string DisplayName, ActorType Type, string? Email, MemberRole Role, Guid? LlmProviderConfigId = null);
public record MemberDto(Guid ActorId, Guid ProjectId, string DisplayName, ActorType Type, string? Email, MemberRole Role, Guid? LlmProviderConfigId);

// ----- LLM provider configs -----
public record CreateLlmProviderRequest(LlmProviderType Type, string Name, string? BaseUrl, string Model, string? ApiKeySecretRef);
public record LlmProviderConfigDto(Guid Id, Guid? ProjectId, LlmProviderType Type, string Name, string? BaseUrl, string Model, bool Enabled);

// ----- Recommend-only agent commentary -----
public record AgentCommentRequest(Guid AgentActorId);

// ----- Observation sources -----
public record CreateSourceRequest(ObservationSourceType Type, string Name, string? ConfigJson);
public record ObservationSourceDto(Guid Id, Guid ProjectId, ObservationSourceType Type, string Name, bool Enabled);

// ----- Observations (capture + inbox) -----
public record IngestObservationRequest(ObservationKind Kind, string Payload, string? ContextMetadata, string? Fingerprint);
public record ObservationDto(
    Guid Id,
    Guid ProjectId,
    Guid SourceId,
    ObservationKind Kind,
    ObservationStatus Status,
    string Payload,
    string? ContextMetadata,
    string? Fingerprint,
    int OccurrenceCount,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    Guid? ChangeRequestId);

public record PromoteObservationRequest(Guid ActorId, string Title, string? Description);
public record DismissObservationRequest(Guid ActorId, string? Reason);
public record ActorActionRequest(Guid ActorId);

// ----- Change requests -----
public record CreateChangeRequest(Guid ActorId, string Title, string? Description, ChangeKind Kind, Priority Priority = Priority.None);
public record ChangeRequestDto(
    Guid Id,
    Guid ProjectId,
    string Title,
    string? Description,
    ChangeKind Kind,
    ChangeStatus Status,
    Priority Priority,
    Guid CreatedByActorId,
    Guid? AssigneeActorId,
    Guid? DuplicateOfId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record ChangeRequestDetailDto(
    ChangeRequestDto Change,
    IReadOnlyList<CommentDto> Comments,
    IReadOnlyList<ObservationDto> Evidence,
    IReadOnlyList<ScmLinkDto> ScmLinks);

public record TransitionChangeRequest(Guid ActorId, ChangeStatus Target, string? Reason, Guid? DuplicateOfId);

public record AddCommentRequest(Guid ActorId, string Body);
public record CommentDto(Guid Id, Guid ChangeRequestId, Guid AuthorActorId, string Body, CommentKind Kind, DateTimeOffset CreatedAt);

public record AddScmLinkRequest(string Url, string? ContextSnapshot);
public record ScmLinkDto(Guid Id, Guid ChangeRequestId, ScmProviderType Provider, string Url, string? ContextSnapshot);

public record AuditEventDto(
    Guid Id,
    AuditEntityType EntityType,
    Guid EntityId,
    Guid ActorId,
    string? FromStatus,
    string? ToStatus,
    string Action,
    string? Reason,
    DateTimeOffset Timestamp);
