import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type { ChangeRequest, ChangeStatus, ObservationStatus, Project } from './types'

export const keys = {
  me: ['me'] as const,
  authConfig: ['authConfig'] as const,
  siteBranding: ['site-branding'] as const,
  users: ['users'] as const,
  projects: ['projects'] as const,
  members: (projectId: string) => ['members', projectId] as const,
  providers: (projectId: string) => ['providers', projectId] as const,
  sources: (projectId: string) => ['sources', projectId] as const,
  actors: (agentsOnly: boolean, includeArchived: boolean) => ['actors', agentsOnly, includeArchived] as const,
  actor: (id: string) => ['actor', id] as const,
  inbox: (projectId: string, status?: ObservationStatus) => ['inbox', projectId, status ?? 'all'] as const,
  changes: (projectId: string, status?: ChangeStatus) => ['changes', projectId, status ?? 'all'] as const,
  change: (id: string) => ['change', id] as const,
  assignmentCounts: ['assignment-counts'] as const,
  audit: (id: string) => ['audit', id] as const,
  assistConversation: (id: string) => ['assist', id] as const,
  pendingAssist: (projectId: string) => ['assist-pending', projectId] as const,
  myAssistConversations: (projectId: string) => ['assist-mine', projectId] as const,
}

/** The auth mode (Local | Oidc) so the login screen renders the right control. Public endpoint. */
export function useAuthConfig() {
  return useQuery({ queryKey: keys.authConfig, queryFn: api.getAuthConfig, staleTime: Infinity })
}

/** The configurable site title (brand). Public endpoint — read on the login screen too. */
export function useSiteBranding() {
  return useQuery({ queryKey: keys.siteBranding, queryFn: api.getSiteBranding, staleTime: Infinity })
}

/** Site-admin: update the site title; refreshes the branding everywhere it's shown. */
export function useUpdateSiteBranding() {
  const qc = useQueryClient()
  return useMutation({
    mutationFn: (body: { title: string }) => api.updateSiteBranding(body),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.siteBranding }),
  })
}

/** The current session. A 401 means "logged out" — don't retry it as a transient failure. */
export function useMe() {
  return useQuery({ queryKey: keys.me, queryFn: api.getMe, retry: false })
}

export function useSiteUsers(enabled: boolean) {
  return useQuery({ queryKey: keys.users, queryFn: api.listUsers, enabled })
}

export function useUserMemberships(userId: string | undefined) {
  return useQuery({
    queryKey: ['user-memberships', userId ?? ''],
    queryFn: () => api.listUserMemberships(userId!),
    enabled: !!userId,
  })
}

export function useProjects(enabled = true, includeArchived = false) {
  return useQuery({
    // Archived projects share the same cache prefix, so invalidating keys.projects refreshes both.
    queryKey: [...keys.projects, includeArchived],
    queryFn: () => api.listProjects(includeArchived),
    enabled,
  })
}

/** Persists the user's personal project order (sidebar order; the top one is their default project).
 * Optimistically reorders every cached project-list variant (active/archived share the keys.projects
 * prefix) so the drag feels instant, rolling back on error, then reconciles. */
export function useReorderProjects() {
  const qc = useQueryClient()

  return useMutation({
    mutationFn: (vars: { orderedIds: string[] }) => api.reorderProjects(vars),
    onMutate: (vars) => {
      const rank = new Map(vars.orderedIds.map((id, i) => [id, i]))
      const previous = qc.getQueriesData<Project[]>({ queryKey: keys.projects })
      // Stable sort: ranked projects first in the new order, unranked (archived/new) keep their order after.
      qc.setQueriesData<Project[]>({ queryKey: keys.projects }, (old) =>
        old ? [...old].sort((a, b) => (rank.get(a.id) ?? Infinity) - (rank.get(b.id) ?? Infinity)) : old,
      )
      void qc.cancelQueries({ queryKey: keys.projects })
      return { previous }
    },
    onError: (_err, _vars, context) => {
      context?.previous?.forEach(([key, data]) => qc.setQueryData(key, data))
    },
    onSettled: () => qc.invalidateQueries({ queryKey: keys.projects }),
  })
}

export function useMembers(projectId: string | undefined) {
  return useQuery({
    queryKey: keys.members(projectId ?? ''),
    queryFn: () => api.listMembers(projectId!),
    enabled: !!projectId,
  })
}

export function useLlmProviders(projectId: string | undefined) {
  return useQuery({
    queryKey: keys.providers(projectId ?? ''),
    queryFn: () => api.listLlmProviders(projectId!),
    enabled: !!projectId,
  })
}

export function useSources(projectId: string | undefined) {
  return useQuery({
    queryKey: keys.sources(projectId ?? ''),
    queryFn: () => api.listSources(projectId!),
    enabled: !!projectId,
  })
}

export function useActors(agentsOnly: boolean, includeArchived = false) {
  return useQuery({
    queryKey: keys.actors(agentsOnly, includeArchived),
    queryFn: () => api.listActors(agentsOnly, includeArchived),
  })
}

export function useActor(id: string | undefined) {
  return useQuery({
    queryKey: keys.actor(id ?? ''),
    queryFn: () => api.getActor(id!),
    enabled: !!id,
  })
}

export function useInbox(projectId: string | undefined, status?: ObservationStatus) {
  return useQuery({
    queryKey: keys.inbox(projectId ?? '', status),
    queryFn: () => api.inbox(projectId!, status),
    enabled: !!projectId,
  })
}

/** A human Capture Assist conversation. Polls as a fallback so replies appear even if the realtime
 * channel is offline; live invalidation (see realtime.ts) makes it feel instant when connected. */
export function useAssistConversation(projectId: string | undefined, conversationId: string | undefined) {
  return useQuery({
    queryKey: keys.assistConversation(conversationId ?? ''),
    queryFn: () => api.getAssistConversation(projectId!, conversationId!),
    enabled: !!projectId && !!conversationId,
    refetchInterval: 5000,
  })
}

/** Assist requests awaiting the current user as the assistant (for inbox/badge surfacing). */
export function usePendingAssistRequests(projectId: string | undefined) {
  return useQuery({
    queryKey: keys.pendingAssist(projectId ?? ''),
    queryFn: () => api.listAssistConversations(projectId!, 'assistant', 'AwaitingAssistant'),
    enabled: !!projectId,
  })
}

/** Capture Assist conversations the current user started (as requester), for the resume surface. */
export function useMyAssistConversations(projectId: string | undefined) {
  return useQuery({
    queryKey: keys.myAssistConversations(projectId ?? ''),
    queryFn: () => api.listAssistConversations(projectId!, 'requester'),
    enabled: !!projectId,
  })
}

export function useChanges(projectId: string | undefined, status?: ChangeStatus) {
  return useQuery({
    queryKey: keys.changes(projectId ?? '', status),
    queryFn: () => api.listChanges(projectId!, status),
    enabled: !!projectId,
  })
}

/** Per-project counts of open change requests assigned to the current user (sidebar indicator). */
export function useMyAssignmentCounts(enabled = true) {
  return useQuery({
    queryKey: keys.assignmentCounts,
    queryFn: api.myAssignmentCounts,
    enabled,
  })
}

export function useChangeDetail(id: string | undefined) {
  return useQuery({
    queryKey: keys.change(id ?? ''),
    queryFn: () => api.getChange(id!),
    enabled: !!id,
  })
}

export function useAudit(id: string | undefined) {
  return useQuery({
    queryKey: keys.audit(id ?? ''),
    queryFn: () => api.audit(id!),
    enabled: !!id,
  })
}

/** Invalidate everything scoped to a project after a mutation (cheap + always correct for pass-1). */
export function useProjectInvalidator(projectId: string | undefined) {
  const qc = useQueryClient()
  return (changeId?: string) => {
    qc.invalidateQueries({ queryKey: ['changes'] })
    qc.invalidateQueries({ queryKey: ['inbox'] })
    // Creation / assignment / transition can change the caller's open-assignment counts (sidebar badge).
    qc.invalidateQueries({ queryKey: keys.assignmentCounts })
    if (projectId) qc.invalidateQueries({ queryKey: keys.projects })
    if (changeId) {
      qc.invalidateQueries({ queryKey: keys.change(changeId) })
      qc.invalidateQueries({ queryKey: keys.audit(changeId) })
    }
  }
}

type TransitionVars = { id: string; target: ChangeStatus; reason?: string | null; duplicateOfId?: string | null }

/** Optimistically moves the card in the board's change list, rolling back if the API rejects
 * (e.g. RBAC 403 / illegal 409), then reconciles with the server. */
export function useTransition(projectId: string | undefined) {
  const qc = useQueryClient()
  const invalidate = useProjectInvalidator(projectId)
  const listKey = keys.changes(projectId ?? '', undefined)

  return useMutation({
    mutationFn: (vars: TransitionVars) =>
      api.transition(vars.id, { target: vars.target, reason: vars.reason, duplicateOfId: vars.duplicateOfId }),
    onMutate: async (vars) => {
      await qc.cancelQueries({ queryKey: listKey })
      const previous = qc.getQueryData<ChangeRequest[]>(listKey)
      if (previous) {
        qc.setQueryData<ChangeRequest[]>(
          listKey,
          previous.map((c) => (c.id === vars.id ? { ...c, status: vars.target } : c)),
        )
      }
      return { previous }
    },
    onError: (_err, _vars, context) => {
      if (context?.previous) qc.setQueryData(listKey, context.previous)
    },
    onSettled: (_data, _err, vars) => invalidate(vars.id),
  })
}

type ReorderVars = { status: ChangeStatus; orderedIds: string[] }

/** Persists a manual within-column board order. Optimistically applies the new SortOrder to the
 * board's change list so the drag feels instant, rolling back on error, then reconciles. */
export function useReorderChanges(projectId: string | undefined) {
  const qc = useQueryClient()
  const invalidate = useProjectInvalidator(projectId)
  const listKey = keys.changes(projectId ?? '', undefined)

  return useMutation({
    mutationFn: (vars: ReorderVars) => api.reorderChanges(projectId!, vars),
    onMutate: (vars) => {
      // Apply the new SortOrder synchronously (before any await) so the optimistic order is committed
      // in the same frame as the drop — a deferred write lets the card paint back at its origin first.
      const previous = qc.getQueryData<ChangeRequest[]>(listKey)
      if (previous) {
        const rank = new Map(vars.orderedIds.map((id, i) => [id, i + 1]))
        qc.setQueryData<ChangeRequest[]>(
          listKey,
          previous.map((c) => (rank.has(c.id) ? { ...c, sortOrder: rank.get(c.id)! } : c)),
        )
      }
      // Cancel any in-flight board refetch so it can't clobber the optimistic order (fire-and-forget).
      void qc.cancelQueries({ queryKey: listKey })
      return { previous }
    },
    onError: (_err, _vars, context) => {
      if (context?.previous) qc.setQueryData(listKey, context.previous)
    },
    onSettled: () => invalidate(),
  })
}
