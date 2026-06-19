import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type { ChangeRequest, ChangeStatus, ObservationStatus } from './types'

export const keys = {
  projects: ['projects'] as const,
  members: (projectId: string) => ['members', projectId] as const,
  providers: (projectId: string) => ['providers', projectId] as const,
  sources: (projectId: string) => ['sources', projectId] as const,
  actors: (agentsOnly: boolean, includeArchived: boolean) => ['actors', agentsOnly, includeArchived] as const,
  inbox: (projectId: string, status?: ObservationStatus) => ['inbox', projectId, status ?? 'all'] as const,
  changes: (projectId: string, status?: ChangeStatus) => ['changes', projectId, status ?? 'all'] as const,
  change: (id: string) => ['change', id] as const,
  audit: (id: string) => ['audit', id] as const,
}

export function useProjects() {
  return useQuery({ queryKey: keys.projects, queryFn: api.listProjects })
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

export function useInbox(projectId: string | undefined, status?: ObservationStatus) {
  return useQuery({
    queryKey: keys.inbox(projectId ?? '', status),
    queryFn: () => api.inbox(projectId!, status),
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
    if (projectId) qc.invalidateQueries({ queryKey: keys.projects })
    if (changeId) {
      qc.invalidateQueries({ queryKey: keys.change(changeId) })
      qc.invalidateQueries({ queryKey: keys.audit(changeId) })
    }
  }
}

type TransitionVars = { id: string; actorId: string; target: ChangeStatus; reason?: string | null; duplicateOfId?: string | null }

/** Optimistically moves the card in the board's change list, rolling back if the API rejects
 * (e.g. RBAC 403 / illegal 409), then reconciles with the server. */
export function useTransition(projectId: string | undefined) {
  const qc = useQueryClient()
  const invalidate = useProjectInvalidator(projectId)
  const listKey = keys.changes(projectId ?? '', undefined)

  return useMutation({
    mutationFn: (vars: TransitionVars) => api.transition(vars.id, vars),
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
