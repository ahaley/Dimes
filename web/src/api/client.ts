import type {
  ActorDetail, ActorSummary, AssistConversation, AssistConversationStatus, AssistConversationSummary, AuthConfig, AuditEvent, CaptureProposal, ChangeKind, ChangeRequest, ChangeRequestDetail, ChangeStatus, ChatTurn, CaptureAssistReply, Comment, ExportInstruction, GenerateProposalsReply,
  BulkTransitionResult,
  LlmProviderConfig, Me, Member, Observation, ObservationSource, ObservationStatus, Priority, Project, ProjectAssignmentCount, ScmLink, SiteBranding, SiteUser, UserMembership,
} from './types'

/** Error carrying the HTTP status + ProblemDetails so the UI can show 403/409 guard failures nicely.
 * status === 0 is a synthetic "couldn't reach the server" (fetch rejected before any HTTP response). */
export class ApiError extends Error {
  status: number
  title: string
  constructor(status: number, title: string, message: string) {
    super(message)
    this.status = status
    this.title = title
  }
}

/** True when an error means the API couldn't be reached (offline / connection refused / dev proxy
 * can't connect) or the server is unhealthy (5xx) — as opposed to a normal 4xx like a 401 logged-out.
 * Lets the UI show an "API unreachable" state instead of mistaking a down backend for a logout. */
export function isConnectivityError(err: unknown): boolean {
  return err instanceof ApiError && (err.status === 0 || err.status >= 500)
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  let res: Response
  try {
    res = await fetch(path, {
      method,
      credentials: 'same-origin', // carry the session cookie
      headers: body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    })
  } catch {
    // fetch rejects (offline / connection refused / dev proxy can't reach the API) before any HTTP
    // status — surface it as status 0 so callers can tell "server unreachable" from an HTTP error.
    throw new ApiError(0, 'Network error', "Couldn't reach the Dimes API.")
  }

  if (!res.ok) {
    let title = res.statusText
    let detail = res.statusText
    try {
      const problem = await res.json()
      title = problem.title ?? title
      detail = problem.detail ?? problem.title ?? detail
    } catch {
      /* non-JSON error body */
    }
    throw new ApiError(res.status, title, detail)
  }

  if (res.status === 204) return undefined as T
  return (await res.json()) as T
}

/** Fetch a file response and trigger a browser download. Filename comes from Content-Disposition. */
async function download(path: string, fallbackName: string): Promise<void> {
  const res = await fetch(path, { credentials: 'same-origin' })
  if (!res.ok) {
    throw new ApiError(res.status, res.statusText, res.statusText)
  }
  const blob = await res.blob()
  const cd = res.headers.get('Content-Disposition') ?? ''
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(cd)
  const name = match ? decodeURIComponent(match[1]) : fallbackName

  const url = URL.createObjectURL(blob)
  const a = document.createElement('a')
  a.href = url
  a.download = name
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}

export const api = {
  // Projects & members
  listProjects: (includeArchived = false) =>
    request<Project[]>('GET', `/api/projects${includeArchived ? '?includeArchived=true' : ''}`),
  createProject: (body: { name: string; description?: string | null; key: string }) =>
    request<Project>('POST', '/api/projects', body),
  updateProject: (id: string, body: { name: string; description?: string | null; sourceControlEnabled: boolean; humanOnly: boolean }) =>
    request<Project>('PATCH', `/api/projects/${id}`, body),
  reorderProjects: (body: { orderedIds: string[] }) =>
    request<void>('POST', '/api/projects/reorder', body),
  archiveProject: (id: string) => request<void>('POST', `/api/projects/${id}/archive`),
  unarchiveProject: (id: string) => request<void>('POST', `/api/projects/${id}/unarchive`),
  listMembers: (projectId: string) => request<Member[]>('GET', `/api/projects/${projectId}/members`),
  addMember: (
    projectId: string,
    body: { displayName: string; type: Member['type']; email?: string | null; role: Member['role']; llmProviderConfigId?: string | null },
  ) => request<Member>('POST', `/api/projects/${projectId}/members`, body),
  updateMember: (
    projectId: string,
    actorId: string,
    body: { displayName: string; email?: string | null; role: Member['role']; llmProviderConfigId?: string | null },
  ) => request<Member>('PATCH', `/api/projects/${projectId}/members/${actorId}`, body),
  removeMember: (projectId: string, actorId: string) =>
    request<void>('DELETE', `/api/projects/${projectId}/members/${actorId}`),
  // Link an existing actor (site user) to a project, or change their role — no new actor created.
  assignMember: (projectId: string, actorId: string, body: { role: Member['role'] }) =>
    request<Member>('PUT', `/api/projects/${projectId}/members/${actorId}`, body),
  listLlmProviders: (projectId: string) =>
    request<LlmProviderConfig[]>('GET', `/api/projects/${projectId}/llm-providers`),
  listSources: (projectId: string) => request<ObservationSource[]>('GET', `/api/projects/${projectId}/sources`),
  createSource: (projectId: string, body: { type: 'Sdk' | 'Seq'; name: string; configJson?: string | null }) =>
    request<ObservationSource>('POST', `/api/projects/${projectId}/sources`, body),
  createLlmProvider: (
    projectId: string,
    body: { type: LlmProviderConfig['type']; name: string; baseUrl?: string | null; model: string; apiKeySecretRef?: string | null },
  ) => request<LlmProviderConfig>('POST', `/api/projects/${projectId}/llm-providers`, body),
  createGlobalLlmProvider: (
    body: { type: LlmProviderConfig['type']; name: string; baseUrl?: string | null; model: string; apiKeySecretRef?: string | null },
  ) => request<LlmProviderConfig>('POST', `/api/llm-providers`, body),
  updateLlmProvider: (
    id: string,
    body: { type: LlmProviderConfig['type']; name: string; baseUrl?: string | null; model: string; apiKeySecretRef?: string | null; enabled: boolean },
  ) => request<LlmProviderConfig>('PATCH', `/api/llm-providers/${id}`, body),
  deleteLlmProvider: (id: string) => request<void>('DELETE', `/api/llm-providers/${id}`),

  // Observations
  ingest: (
    sourceId: string,
    body: { kind: Observation['kind']; payload: string; contextMetadata?: string | null; fingerprint?: string | null },
  ) => request<Observation>('POST', `/api/sources/${sourceId}/observations`, body),
  inbox: (projectId: string, status?: ObservationStatus) =>
    request<Observation[]>('GET', `/api/projects/${projectId}/observations${status ? `?status=${status}` : ''}`),
  clusterObservation: (id: string) =>
    request<Observation>('POST', `/api/observations/${id}/cluster`),
  dismissObservation: (id: string, reason?: string | null) =>
    request<Observation>('POST', `/api/observations/${id}/dismiss`, { reason }),
  promoteObservation: (id: string, body: { title: string; description?: string | null }) =>
    request<ChangeRequest>('POST', `/api/observations/${id}/promote`, body),

  // Change requests
  createChange: (
    projectId: string,
    body: { title: string; description?: string | null; kind: ChangeKind; priority?: Priority; assigneeActorId?: string | null },
  ) => request<ChangeRequest>('POST', `/api/projects/${projectId}/changes`, body),
  createChangesBatch: (projectId: string, body: { changes: CaptureProposal[] }) =>
    request<ChangeRequest[]>('POST', `/api/projects/${projectId}/changes/batch`, body),
  reorderChanges: (projectId: string, body: { status: ChangeStatus; orderedIds: string[] }) =>
    request<void>('POST', `/api/projects/${projectId}/changes/reorder`, body),
  listChanges: (projectId: string, status?: ChangeStatus) =>
    request<ChangeRequest[]>('GET', `/api/projects/${projectId}/changes${status ? `?status=${status}` : ''}`),
  getChange: (id: string) => request<ChangeRequestDetail>('GET', `/api/changes/${id}`),
  myAssignmentCounts: () =>
    request<ProjectAssignmentCount[]>('GET', '/api/me/assignment-counts'),
  updateChangeDetails: (
    id: string,
    body: { title: string; description?: string | null; priority: Priority },
  ) => request<ChangeRequest>('PATCH', `/api/changes/${id}`, body),
  assignChange: (id: string, body: { assigneeActorId?: string | null }) =>
    request<ChangeRequest>('PATCH', `/api/changes/${id}/assignee`, body),
  transition: (id: string, body: { target: ChangeStatus; reason?: string | null; duplicateOfId?: string | null }) =>
    request<ChangeRequest>('POST', `/api/changes/${id}/transition`, body),
  addEpicChild: (epicId: string, childId: string) =>
    request<ChangeRequest>('POST', `/api/changes/${epicId}/children`, { childId }),
  removeEpicChild: (epicId: string, childId: string) =>
    request<ChangeRequest>('DELETE', `/api/changes/${epicId}/children/${childId}`),
  bulkTransition: (epicId: string, body: { target: ChangeStatus; reason?: string | null }) =>
    request<BulkTransitionResult>('POST', `/api/changes/${epicId}/bulk-transition`, body),
  addComment: (id: string, body: { body: string }) =>
    request<Comment>('POST', `/api/changes/${id}/comments`, body),
  addScmLink: (id: string, body: { url: string; contextSnapshot?: string | null }) =>
    request<ScmLink>('POST', `/api/changes/${id}/scm-links`, body),
  agentComment: (id: string, agentActorId: string) =>
    request<Comment>('POST', `/api/changes/${id}/agent-comment`, { agentActorId }),
  captureAssistChat: (
    projectId: string,
    body: { agentActorId: string; draft?: string | null; messages: ChatTurn[] },
  ) => request<CaptureAssistReply>('POST', `/api/projects/${projectId}/capture-assist/chat`, body),
  generateProposals: (projectId: string, body: { agentActorId: string; markdown: string }) =>
    request<GenerateProposalsReply>('POST', `/api/projects/${projectId}/capture-assist/proposals`, body),

  // Capture Assist with a human assistant (persisted, two-way)
  startAssistConversation: (
    projectId: string,
    body: { assistantActorId: string; draft?: string | null; title?: string | null; message: string },
  ) => request<AssistConversation>('POST', `/api/projects/${projectId}/assist/conversations`, body),
  getAssistConversation: (projectId: string, conversationId: string) =>
    request<AssistConversation>('GET', `/api/projects/${projectId}/assist/conversations/${conversationId}`),
  listAssistConversations: (projectId: string, role: 'assistant' | 'requester', status?: AssistConversationStatus) =>
    request<AssistConversationSummary[]>(
      'GET',
      `/api/projects/${projectId}/assist/conversations?role=${role}${status ? `&status=${status}` : ''}`,
    ),
  postAssistMessage: (projectId: string, conversationId: string, body: { body: string }) =>
    request<AssistConversation>('POST', `/api/projects/${projectId}/assist/conversations/${conversationId}/messages`, body),
  closeAssistConversation: (projectId: string, conversationId: string, body: { changeRequestId?: string | null }) =>
    request<AssistConversation>('POST', `/api/projects/${projectId}/assist/conversations/${conversationId}/close`, body),
  audit: (id: string) => request<AuditEvent[]>('GET', `/api/changes/${id}/audit`),

  // Actors (app-level)
  listActors: (agentsOnly: boolean, includeArchived = false) =>
    request<ActorSummary[]>('GET', `/api/actors?agentsOnly=${agentsOnly}&includeArchived=${includeArchived}`),
  getActor: (id: string) => request<ActorDetail>('GET', `/api/actors/${id}`),
  updateActor: (id: string, body: { displayName: string; email?: string | null }) =>
    request<ActorSummary>('PATCH', `/api/actors/${id}`, body),
  archiveActor: (id: string) => request<void>('POST', `/api/actors/${id}/archive`),
  unarchiveActor: (id: string) => request<void>('POST', `/api/actors/${id}/unarchive`),
  deleteActor: (id: string) => request<void>('DELETE', `/api/actors/${id}`),

  // Export
  exportInDevelopment: (projectId: string) =>
    download(`/api/projects/${projectId}/export/in-development`, 'in-development.md'),
  getExportInstruction: (projectId: string) =>
    request<ExportInstruction>('GET', `/api/projects/${projectId}/export/instruction`),
  updateExportInstruction: (projectId: string, body: { content: string }) =>
    request<ExportInstruction>('PUT', `/api/projects/${projectId}/export/instruction`, body),

  // Site branding (public read; site-admin write)
  getSiteBranding: () => request<SiteBranding>('GET', '/api/config/branding'),
  updateSiteBranding: (body: { title: string }) =>
    request<SiteBranding>('PUT', '/api/admin/branding', body),

  // Authentication
  getAuthConfig: () => request<AuthConfig>('GET', '/api/auth/config'),
  getMe: () => request<Me>('GET', '/api/auth/me'),
  login: (body: { email: string; password: string }) => request<Me>('POST', '/api/auth/login', body),
  logout: () => request<void>('POST', '/api/auth/logout'),

  // Site administration (site-admin only)
  listUsers: () => request<SiteUser[]>('GET', '/api/admin/users'),
  createLocalUser: (body: { displayName: string; email: string; password?: string | null; isSiteAdmin: boolean }) =>
    request<SiteUser>('POST', '/api/admin/users', body),
  listUserMemberships: (id: string) => request<UserMembership[]>('GET', `/api/admin/users/${id}/memberships`),
  assignUserMembership: (id: string, body: { projectId: string; role: Member['role'] }) =>
    request<void>('POST', `/api/admin/users/${id}/memberships`, body),
  removeUserMembership: (id: string, projectId: string) =>
    request<void>('DELETE', `/api/admin/users/${id}/memberships/${projectId}`),
  updateUser: (id: string, body: { displayName: string; email?: string | null }) =>
    request<SiteUser>('PATCH', `/api/admin/users/${id}`, body),
  resetPassword: (id: string, body: { password: string }) =>
    request<void>('POST', `/api/admin/users/${id}/reset-password`, body),
  setSiteAdmin: (id: string, body: { isSiteAdmin: boolean }) =>
    request<SiteUser>('POST', `/api/admin/users/${id}/site-admin`, body),
  archiveUser: (id: string) => request<void>('POST', `/api/admin/users/${id}/archive`),
  unarchiveUser: (id: string) => request<void>('POST', `/api/admin/users/${id}/unarchive`),
  deleteUser: (id: string) => request<void>('DELETE', `/api/admin/users/${id}`),
}
