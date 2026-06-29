using Dimes.Domain;

namespace Dimes.Api.Contracts;

// NOTE: the acting actor is derived from the authenticated session (cookie → ICurrentActor), not the
// request body. Mutating DTOs therefore carry no ActorId; controllers pass currentActor.ActorId into
// services, which still authorize via MembershipResolver.

// ----- Projects & members -----
// Key is optional over the wire: the web form always supplies one, but when absent the server derives a
// unique key from the name (so legacy/automation callers still get a valid display-id prefix).
public record CreateProjectRequest(string Name, string? Description, string? Key = null);
// A user's personal top-to-bottom ordering of their visible projects (drives the sidebar + default project).
public record ReorderProjectsRequest(IReadOnlyList<Guid> OrderedIds);
public record UpdateProjectRequest(string Name, string? Description, bool SourceControlEnabled, bool HumanOnly);
public record ProjectDto(Guid Id, string Name, string? Description, DateTimeOffset CreatedAt, bool IsArchived, DateTimeOffset? ArchivedAt, bool SourceControlEnabled, bool HumanOnly, string? Key);

public record AddMemberRequest(string DisplayName, ActorType Type, string? Email, MemberRole Role, Guid? LlmProviderConfigId = null);
public record UpdateMemberRequest(string DisplayName, string? Email, MemberRole Role, Guid? LlmProviderConfigId);
public record MemberDto(Guid ActorId, Guid ProjectId, string DisplayName, ActorType Type, string? Email, MemberRole Role, Guid? LlmProviderConfigId);
// Link an existing actor (a site user) to a project, or change their role — pure membership, no new actor.
public record SetMemberRoleRequest(MemberRole Role);

// ----- Site branding (public read; site-admin write) -----
// The customizable site title shown in the brand wordmark, login screen and browser tab.
public record SiteBrandingDto(string Title);
public record UpdateSiteBrandingRequest(string Title);

// ----- Authentication -----
// Mode is a deployment choice (Local | Oidc); the SPA reads it to render the right login UI.
public record AuthConfigDto(string Mode);
public record LoginRequest(string Email, string Password);
public record MeDto(Guid ActorId, string DisplayName, string? Email, bool IsSiteAdmin);

// ----- Site administration (app-level user management; site-admin only) -----
public record SiteUserDto(Guid Id, string DisplayName, string? Email, ActorType Type, bool IsSiteAdmin, bool HasLocalCredential, bool IsArchived, bool Deletable);
// Password is optional: a user can be pre-provisioned (or OIDC-only) and given a password later.
public record CreateLocalUserRequest(string DisplayName, string Email, string? Password, bool IsSiteAdmin);
public record ResetPasswordRequest(string Password);
public record SetSiteAdminRequest(bool IsSiteAdmin);

// A user's project assignments (managed from the Site Users screen).
public record UserMembershipDto(Guid ProjectId, string ProjectName, MemberRole Role);
public record AssignMembershipRequest(Guid ProjectId, MemberRole Role);

// ----- Actors (app-level management) -----
public record ActorDto(
    Guid Id, string DisplayName, ActorType Type, string? Email,
    Guid? LlmProviderConfigId, string? ProviderName, int ProjectCount, bool Deletable, bool IsArchived);
public record UpdateActorRequest(string DisplayName, string? Email);

// Actor-centric "presentation": identity + provider binding + the actor's per-project memberships
// (project + role), so an agent's role and project assignments are visible in one place. Memberships
// reuses UserMembershipDto (ProjectId, ProjectName, Role).
public record ActorDetailDto(
    Guid Id, string DisplayName, ActorType Type, string? Email,
    Guid? LlmProviderConfigId, string? ProviderName, bool Deletable, bool IsArchived,
    IReadOnlyList<UserMembershipDto> Memberships);

// ----- LLM provider configs -----
public record CreateLlmProviderRequest(LlmProviderType Type, string Name, string? BaseUrl, string Model, string? ApiKeySecretRef);
public record UpdateLlmProviderRequest(LlmProviderType Type, string Name, string? BaseUrl, string Model, string? ApiKeySecretRef, bool Enabled);
// ApiKeySecretRef is a non-sensitive reference name (e.g. "ANTHROPIC_KEY"), not the secret itself —
// safe to expose so the edit form can prefill it.
public record LlmProviderConfigDto(Guid Id, Guid? ProjectId, LlmProviderType Type, string Name, string? BaseUrl, string Model, string? ApiKeySecretRef, bool Enabled);

// ----- Recommend-only agent commentary -----
public record AgentCommentRequest(Guid AgentActorId);

// ----- Capture Assist (conversational change-request drafting; stateless/ephemeral) -----
// The conversation is held client-side and replayed each turn; the server persists nothing here.
// Role is "user" or "assistant".
public record ChatTurn(string Role, string Content);
public record CaptureAssistChatRequest(Guid AgentActorId, string? Draft, IReadOnlyList<ChatTurn> Messages);
public record CaptureAssistReplyDto(string Reply);

// ----- Capture Assist Freestyle Mode (markdown brief -> structured change-order proposals) -----
// Also stateless and recommend-only: the agent's LLM decomposes a freeform markdown brief into a list
// of proposed changes the user can edit before confirming. Proposal shape mirrors CreateChangeRequest
// so the confirm step maps 1:1 to a batch create.
public record CaptureProposalDto(string Title, string? Description, ChangeKind Kind, Priority Priority);
public record GenerateProposalsRequest(Guid AgentActorId, string Markdown);
public record GenerateProposalsReplyDto(IReadOnlyList<CaptureProposalDto> Proposals);

// ----- Capture Assist with a HUMAN assistant (persisted, two-way) -----
// Unlike the stateless AI chat above, a human assistant conversation is persisted and bubbled into the
// assistant's observation inbox. The acting actor (requester / replier) comes from the session.
public record StartAssistConversationRequest(Guid AssistantActorId, string? Draft, string? Title, string Message);
public record PostAssistMessageRequest(string Body);
public record CloseAssistConversationRequest(Guid? ChangeRequestId);
public record AssistMessageDto(
    Guid Id, Guid ConversationId, Guid AuthorActorId, AssistMessageSender Sender, string Body, DateTimeOffset CreatedAt);
public record AssistConversationDto(
    Guid Id, Guid ProjectId,
    Guid RequesterActorId, string RequesterName,
    Guid AssistantActorId, string AssistantName,
    AssistConversationStatus Status, string? Title, string? Draft,
    Guid? ChangeRequestId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<AssistMessageDto> Messages);
public record AssistConversationSummaryDto(
    Guid Id, Guid ProjectId,
    Guid RequesterActorId, string RequesterName,
    Guid AssistantActorId, string AssistantName,
    AssistConversationStatus Status, string? Title, string? LastMessagePreview,
    int MessageCount, DateTimeOffset UpdatedAt);

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
    Guid? ChangeRequestId,
    Guid? TargetActorId);

// The acting actor is derived from the authenticated session, not the request body.
public record PromoteObservationRequest(string Title, string? Description);
public record DismissObservationRequest(string? Reason);

// ----- Change requests -----
public record CreateChangeRequest(string Title, string? Description, ChangeKind Kind, Priority Priority = Priority.None, Guid? AssigneeActorId = null);
// Confirm a batch of Freestyle-Mode proposals: each element is a normal create, all land in Captured.
public record CreateChangesRequest(IReadOnlyList<CreateChangeRequest> Changes);
// Persist a manual within-column order (board drag-and-drop): the full set of change ids in that
// status, in their new top-to-bottom order.
public record ReorderChangesRequest(ChangeStatus Status, IReadOnlyList<Guid> OrderedIds);
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
    DateTimeOffset UpdatedAt,
    int SortOrder,
    int? Number,
    string? DisplayKey,
    DateTimeOffset? CompletedAt,
    // When set, this change is a composed child of the referenced Epic (null for standalone / an Epic).
    Guid? ParentChangeRequestId);

public record ChangeRequestDetailDto(
    ChangeRequestDto Change,
    IReadOnlyList<CommentDto> Comments,
    IReadOnlyList<ObservationDto> Evidence,
    IReadOnlyList<ScmLinkDto> ScmLinks,
    // The change requests composed under this one (only non-empty for an Epic).
    IReadOnlyList<ChangeRequestDto> Children);

// Per-project count of the current actor's open (non-terminal) assigned change requests — drives the
// sidebar "assigned to you" indicator. Only projects with a non-zero count are returned.
public record ProjectAssignmentCountDto(Guid ProjectId, int Count);

public record TransitionChangeRequest(ChangeStatus Target, string? Reason, Guid? DuplicateOfId);

/// <summary>Post-hoc edit of a change's free-form details (author or Maintainer only). Recipient
/// assignment is a separate action — see <see cref="AssignChangeRequest"/>.</summary>
public record UpdateChangeDetailsRequest(string Title, string? Description, Priority Priority);

/// <summary>Set or clear a change's recipient (AssigneeActorId). Requires Contributor+; the recipient,
/// when non-null, must be a project member. Null clears it.</summary>
public record AssignChangeRequest(Guid? AssigneeActorId);

/// <summary>A generated file ready to download (name + UTF-8 text content).</summary>
public record MarkdownExport(string FileName, string Markdown);

/// <summary>A project's editable export "work order" guidance. <paramref name="IsDefault"/> is true when
/// the project has no stored override and the built-in default is in effect.</summary>
public record ExportInstructionDto(string Content, bool IsDefault);

/// <summary>Set a project's export guidance. A blank body resets to the built-in default.</summary>
public record UpdateExportInstructionRequest(string Content);

public record AddCommentRequest(string Body);
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
