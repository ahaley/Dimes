// TypeScript mirror of the Dimes API contracts. Hand-written for pass-1; a future slice can
// generate these from the ASP.NET OpenAPI document (openapi-typescript) to keep them in lockstep.

export type ActorType = 'Human' | 'Agent'
export type MemberRole = 'Assistant' | 'Reporter' | 'Contributor' | 'Maintainer'
export type ObservationSourceType = 'Sdk' | 'Seq' | 'Internal'
export type ObservationKind = 'ExplicitFeedback' | 'SolicitedFeedback' | 'BehavioralFriction' | 'TechnicalError' | 'AssistRequest'
export type ObservationStatus = 'New' | 'Clustered' | 'Promoted' | 'Dismissed'
export type AssistConversationStatus = 'AwaitingAssistant' | 'AwaitingRequester' | 'Closed'
export type AssistMessageSender = 'Requester' | 'Assistant'
export type ChangeKind = 'Problem' | 'Feature' | 'ObservationDriven'
export type ChangeStatus =
  | 'Captured' | 'Triaged' | 'Approved' | 'InDevelopment' | 'InReview' | 'Done' | 'Rejected' | 'Duplicate'
export type Priority = 'None' | 'Low' | 'Medium' | 'High' | 'Critical'
export type CommentKind = 'Human' | 'AgentRecommendation'
export type LlmProviderType = 'Anthropic' | 'OpenAICompatible'
export type ScmProviderType = 'GitHub'
export type AuditEntityType = 'ChangeRequest' | 'Observation'

export interface Project { id: string; name: string; description?: string | null; createdAt: string; isArchived: boolean; archivedAt?: string | null; sourceControlEnabled: boolean; humanOnly: boolean; key?: string | null }
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
}
// Per-project count of open change requests assigned to the current user (sidebar indicator).
export interface ProjectAssignmentCount {
  projectId: string; count: number
}
export interface LlmProviderConfig {
  id: string; projectId?: string | null; type: LlmProviderType; name: string
  baseUrl?: string | null; model: string; apiKeySecretRef?: string | null; enabled: boolean
}
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

// ----- Site branding -----
export interface SiteBranding { title: string }

// ----- Authentication -----
export type AuthMode = 'Local' | 'Oidc'
export interface AuthConfig { mode: AuthMode }
export interface Me { actorId: string; displayName: string; email?: string | null; isSiteAdmin: boolean }
export interface SiteUser {
  id: string; displayName: string; email?: string | null; type: ActorType
  isSiteAdmin: boolean; hasLocalCredential: boolean; isArchived: boolean; deletable: boolean
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
