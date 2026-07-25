// TypeScript mirror of the Dimes API contracts. Hand-written for pass-1; a future slice can
// generate these from the ASP.NET OpenAPI document (openapi-typescript) to keep them in lockstep.

export type ActorType = 'Human' | 'Agent'
export type MemberRole = 'Assistant' | 'Reporter' | 'Contributor' | 'Maintainer'
export type ObservationSourceType = 'Sdk' | 'Seq' | 'Internal'
export type ObservationKind = 'ExplicitFeedback' | 'SolicitedFeedback' | 'BehavioralFriction' | 'TechnicalError' | 'AssistRequest'
export type ObservationStatus = 'New' | 'Clustered' | 'Promoted' | 'Dismissed'
export type AssistConversationStatus = 'AwaitingAssistant' | 'AwaitingRequester' | 'Closed'
export type AssistMessageSender = 'Requester' | 'Assistant'
export type ChangeKind = 'Problem' | 'Feature' | 'ObservationDriven' | 'Epic' | 'Chore'
export type ChangeStatus =
  | 'Captured' | 'Triaged' | 'Approved' | 'InDevelopment' | 'InReview' | 'Done' | 'Rejected' | 'Duplicate'
export type Priority = 'None' | 'Low' | 'Medium' | 'High' | 'Critical'
export type CommentKind = 'Human' | 'AgentRecommendation'
export type LlmProviderType = 'Anthropic' | 'OpenAICompatible'
export type ScmProviderType = 'GitHub'
export type AuditEntityType = 'ChangeRequest' | 'Observation'
export type WorkOrderItemStatus = 'Pending' | 'Reported' | 'Blocked' | 'Confirmed'
export type NotificationChannelType = 'GoogleChat'
export type NotificationEventType =
  | 'AwaitingApproval' | 'AssignedToYou' | 'WorkOrderResults' | 'ChangeTransitioned' | 'AssistReply' | 'DailyDigest'

// myRole is your own role in the project, or null when you hold no membership in it (only a site
// admin, who sees every project, ever gets a null).
// createdBy* is the project's provenance — who is answerable for it existing. Null on projects created
// before creation was attributed. Display name only; the creator's email is never exposed here.
export interface Project {
  id: string; name: string; description?: string | null; createdAt: string
  isArchived: boolean; archivedAt?: string | null; sourceControlEnabled: boolean; humanOnly: boolean
  key?: string | null; myRole?: MemberRole | null
  createdByActorId?: string | null; createdByDisplayName?: string | null
}
export interface Member {
  actorId: string; projectId: string; displayName: string; type: ActorType
  email?: string | null; role: MemberRole; llmProviderConfigId?: string | null
}
export interface ObservationSource {
  id: string; projectId: string; type: ObservationSourceType; name: string; enabled: boolean
}
export interface Observation {
  id: string; projectId: string; sourceId: string; kind: ObservationKind; status: ObservationStatus
  payload: string; contextMetadata?: string | null; fingerprint?: string | null
  occurrenceCount: number; firstSeen: string; lastSeen: string; changeRequestId?: string | null
  targetActorId?: string | null
}
export interface ChangeRequest {
  id: string; projectId: string; title: string; description?: string | null
  kind: ChangeKind; status: ChangeStatus; priority: Priority
  createdByActorId: string; assigneeActorId?: string | null; duplicateOfId?: string | null
  createdAt: string; updatedAt: string; sortOrder: number; number?: number | null; displayKey?: string | null
  completedAt?: string | null
  // When set, this change is a composed child of the referenced Epic (null for standalone / an Epic).
  parentChangeRequestId?: string | null
  // The most recent work-order report for this change (null if never exported / not yet reported back).
  // Only the read paths (list, detail) populate these; mutation responses omit them.
  workOrderStatus?: WorkOrderItemStatus | null
  workOrderReportedAt?: string | null
}
export interface Comment {
  id: string; changeRequestId: string; authorActorId: string; body: string; kind: CommentKind; createdAt: string
}
export interface ScmLink {
  id: string; changeRequestId: string; provider: ScmProviderType; url: string; contextSnapshot?: string | null
}
export interface AuditEvent {
  id: string; entityType: AuditEntityType; entityId: string; actorId: string
  fromStatus?: string | null; toStatus?: string | null; action: string; reason?: string | null; timestamp: string
}
export interface ChangeRequestDetail {
  change: ChangeRequest; comments: Comment[]; evidence: Observation[]; scmLinks: ScmLink[]
  // The change requests composed under this one (only non-empty for an Epic).
  children: ChangeRequest[]
}
// Per-project count of open change requests assigned to the current user (sidebar indicator).
export interface ProjectAssignmentCount {
  projectId: string; count: number
}
// A project's most recent work-order export and how much of it has reported back. `pendingChangeIds`
// are the changes still out with an agent — they drive the re-export warning.
export interface WorkOrderSummary {
  id: string; fileName: string; exportedAt: string; exportedByActorId: string
  itemCount: number; reportedCount: number; blockedCount: number; pendingChangeIds: string[]
}
export interface LlmProviderConfig {
  id: string; projectId?: string | null; type: LlmProviderType; name: string
  baseUrl?: string | null; model: string; apiKeySecretRef?: string | null; enabled: boolean
}
// ----- Notification channels (per-project outbound) -----
// secretRef is a non-sensitive reference name (e.g. "GCHAT_CREDS"), safe to prefill in the edit form.
export interface NotificationChannel {
  id: string; projectId: string; type: NotificationChannelType; name: string; target: string
  secretRef?: string | null; events: NotificationEventType[]; enabled: boolean
  lastDeliveryAt?: string | null; lastDeliveryOk?: boolean | null; lastDeliveryError?: string | null
}
// The current user's own digest opt-out for a project.
export interface NotificationPreference { digestOptOut: boolean }

export interface ActorSummary {
  id: string; displayName: string; type: ActorType; email?: string | null
  llmProviderConfigId?: string | null; providerName?: string | null
  projectCount: number; deletable: boolean; isArchived: boolean
}
// Actor-centric presentation: identity + provider + per-project roles in one place.
export interface ActorDetail {
  id: string; displayName: string; type: ActorType; email?: string | null
  llmProviderConfigId?: string | null; providerName?: string | null
  deletable: boolean; isArchived: boolean; memberships: UserMembership[]
}

// ----- Export -----
// A project's editable export "work order" guidance. `isDefault` is true when no override is stored
// and the built-in default is in effect.
export interface ExportInstruction { content: string; isDefault: boolean }

// ----- Site branding -----
export interface SiteBranding { title: string }

// ----- Project creation quota -----
// The caller's own allowance. `used` counts every project they created, archived ones included — archiving
// deliberately does not free a slot. Site admins report `unlimited` (their `limit` is meaningless).
// `limitIsPersonal` distinguishes a limit set on this user from one inherited from the site policy. It
// only matters at 0, where the two mean different things and point at different remedies.
export interface ProjectQuota {
  used: number; limit: number; canCreate: boolean; unlimited: boolean; limitIsPersonal: boolean
}
// The site-wide default for users without an override. 0 restricts creation to site admins. Admin-only.
export interface ProjectPolicy { projectLimit: number }

// ----- Authentication -----
export type AuthMode = 'Local' | 'Oidc'
export interface AuthConfig { mode: AuthMode }
export interface Me { actorId: string; displayName: string; email?: string | null; isSiteAdmin: boolean }
// `projectLimit` is the user's personal creation override (null = inherit the site policy);
// `projectsCreated` is how much of it they've used, so the admin table can show "2 / 3".
export interface SiteUser {
  id: string; displayName: string; email?: string | null; type: ActorType
  isSiteAdmin: boolean; hasLocalCredential: boolean; isArchived: boolean; deletable: boolean
  projectLimit?: number | null; projectsCreated: number
}
export interface UserMembership { projectId: string; projectName: string; role: MemberRole }

// ----- Capture Assist (ephemeral conversational drafting with an AI agent) -----
export interface ChatTurn { role: 'user' | 'assistant'; content: string }
export interface CaptureAssistReply { reply: string }

// ----- Capture Assist Freestyle Mode (markdown brief -> editable change-order proposals) -----
export interface CaptureProposal { title: string; description?: string | null; kind: ChangeKind; priority: Priority }
export interface GenerateProposalsReply { proposals: CaptureProposal[] }

// ----- Capture Assist with a human assistant (persisted, two-way) -----
export interface AssistMessage {
  id: string; conversationId: string; authorActorId: string
  sender: AssistMessageSender; body: string; createdAt: string
}
export interface AssistConversation {
  id: string; projectId: string
  requesterActorId: string; requesterName: string
  assistantActorId: string; assistantName: string
  status: AssistConversationStatus; title?: string | null; draft?: string | null
  changeRequestId?: string | null; createdAt: string; updatedAt: string
  messages: AssistMessage[]
}
export interface AssistConversationSummary {
  id: string; projectId: string
  requesterActorId: string; requesterName: string
  assistantActorId: string; assistantName: string
  status: AssistConversationStatus; title?: string | null; lastMessagePreview?: string | null
  messageCount: number; updatedAt: string
}

// The ordered "happy path" of the change lifecycle, for board columns.
export const LIFECYCLE_COLUMNS: ChangeStatus[] = [
  'Captured', 'Triaged', 'Approved', 'InDevelopment', 'InReview', 'Done',
]
