import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from './client'
import type { ChangeStatus, ObservationStatus } from './types'

export const keys = {
  projects: ['projects'] as const,
  members: (projectId: string) => ['members', projectId] as const,
  providers: (projectId: string) => ['providers', projectId] as const,
  sources: (projectId: string) => ['sources', projectId] as const,
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

export function useTransition(projectId: string | undefined) {
  const invalidate = useProjectInvalidator(projectId)
  return useMutation({
    mutationFn: (vars: { id: string; actorId: string; target: ChangeStatus; reason?: string | null; duplicateOfId?: string | null }) =>
      api.transition(vars.id, vars),
    onSuccess: (_data, vars) => invalidate(vars.id),
  })
}
