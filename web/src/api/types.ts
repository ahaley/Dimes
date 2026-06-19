// TypeScript mirror of the Dimes API contracts. Hand-written for pass-1; a future slice can
// generate these from the ASP.NET OpenAPI document (openapi-typescript) to keep them in lockstep.

export type ActorType = 'Human' | 'Agent'
export type MemberRole = 'Reporter' | 'Contributor' | 'Maintainer'
export type ObservationSourceType = 'Sdk' | 'Seq'
export type ObservationKind = 'ExplicitFeedback' | 'SolicitedFeedback' | 'BehavioralFriction' | 'TechnicalError'
export type ObservationStatus = 'New' | 'Clustered' | 'Promoted' | 'Dismissed'
export type ChangeKind = 'Problem' | 'Feature' | 'ObservationDriven'
export type ChangeStatus =
  | 'Captured' | 'Triaged' | 'Approved' | 'InDevelopment' | 'InReview' | 'Done' | 'Rejected' | 'Duplicate'
export type Priority = 'None' | 'Low' | 'Medium' | 'High' | 'Critical'
export type CommentKind = 'Human' | 'AgentRecommendation'
export type LlmProviderType = 'Anthropic' | 'OpenAICompatible'
export type ScmProviderType = 'GitHub'
export type AuditEntityType = 'ChangeRequest' | 'Observation'

export interface Project { id: string; name: string; description?: string | null; createdAt: string }
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
}
export interface ChangeRequest {
  id: string; projectId: string; title: string; description?: string | null
  kind: ChangeKind; status: ChangeStatus; priority: Priority
  createdByActorId: string; assigneeActorId?: string | null; duplicateOfId?: string | null
  createdAt: string; updatedAt: string
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
export interface LlmProviderConfig {
  id: string; projectId?: string | null; type: LlmProviderType; name: string
  baseUrl?: string | null; model: string; apiKeySecretRef?: string | null; enabled: boolean
}
export interface ActorSummary {
  id: string; displayName: string; type: ActorType; email?: string | null
  llmProviderConfigId?: string | null; providerName?: string | null
  projectCount: number; deletable: boolean
}

// The ordered "happy path" of the change lifecycle, for board columns.
export const LIFECYCLE_COLUMNS: ChangeStatus[] = [
  'Captured', 'Triaged', 'Approved', 'InDevelopment', 'InReview', 'Done',
]
