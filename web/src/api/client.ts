import type {
  ActorSummary, AuditEvent, ChangeKind, ChangeRequest, ChangeRequestDetail, ChangeStatus, Comment, LlmProviderConfig,
  Member, Observation, ObservationSource, ObservationStatus, Priority, Project, ScmLink,
} from './types'

/** Error carrying the HTTP status + ProblemDetails so the UI can show 403/409 guard failures nicely. */
export class ApiError extends Error {
  status: number
  title: string
  constructor(status: number, title: string, message: string) {
    super(message)
    this.status = status
    this.title = title
  }
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const res = await fetch(path, {
    method,
    headers: body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

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
  const res = await fetch(path)
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
  listProjects: () => request<Project[]>('GET', '/api/projects'),
  createProject: (body: { name: string; description?: string | null }) =>
    request<Project>('POST', '/api/projects', body),
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
  clusterObservation: (id: string, actorId: string) =>
    request<Observation>('POST', `/api/observations/${id}/cluster`, { actorId }),
  dismissObservation: (id: string, actorId: string, reason?: string | null) =>
    request<Observation>('POST', `/api/observations/${id}/dismiss`, { actorId, reason }),
  promoteObservation: (id: string, body: { actorId: string; title: string; description?: string | null }) =>
    request<ChangeRequest>('POST', `/api/observations/${id}/promote`, body),

  // Change requests
  createChange: (
    projectId: string,
    body: { actorId: string; title: string; description?: string | null; kind: ChangeKind; priority?: Priority },
  ) => request<ChangeRequest>('POST', `/api/projects/${projectId}/changes`, body),
  listChanges: (projectId: string, status?: ChangeStatus) =>
    request<ChangeRequest[]>('GET', `/api/projects/${projectId}/changes${status ? `?status=${status}` : ''}`),
  getChange: (id: string) => request<ChangeRequestDetail>('GET', `/api/changes/${id}`),
  updateChangeDetails: (
    id: string,
    body: { actorId: string; title: string; description?: string | null; priority: Priority },
  ) => request<ChangeRequest>('PATCH', `/api/changes/${id}`, body),
  transition: (id: string, body: { actorId: string; target: ChangeStatus; reason?: string | null; duplicateOfId?: string | null }) =>
    request<ChangeRequest>('POST', `/api/changes/${id}/transition`, body),
  addComment: (id: string, body: { actorId: string; body: string }) =>
    request<Comment>('POST', `/api/changes/${id}/comments`, body),
  addScmLink: (id: string, body: { url: string; contextSnapshot?: string | null }) =>
    request<ScmLink>('POST', `/api/changes/${id}/scm-links`, body),
  agentComment: (id: string, agentActorId: string) =>
    request<Comment>('POST', `/api/changes/${id}/agent-comment`, { agentActorId }),
  audit: (id: string) => request<AuditEvent[]>('GET', `/api/changes/${id}/audit`),

  // Actors (app-level)
  listActors: (agentsOnly: boolean) =>
    request<ActorSummary[]>('GET', `/api/actors?agentsOnly=${agentsOnly}`),
  deleteActor: (id: string) => request<void>('DELETE', `/api/actors/${id}`),

  // Export
  exportInDevelopment: (projectId: string) =>
    download(`/api/projects/${projectId}/export/in-development`, 'in-development.md'),
}
