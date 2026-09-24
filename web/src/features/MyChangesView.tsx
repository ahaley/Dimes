import { useMemo, useState, type ReactNode } from 'react'
import { useNavigate } from 'react-router-dom'
import { useMyChanges } from '../api/hooks'
import { LIFECYCLE_COLUMNS, type ChangeStatus, type Project } from '../api/types'
import { STATUS_TONE, initials, projectColor, relativeTime } from '../lifecycle'
import { Badge, Card, cx } from '../components/ui'

const OPEN_STATUSES: ChangeStatus[] = LIFECYCLE_COLUMNS.filter((s) => s !== 'Done')

function Chip({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      onClick={onClick}
      aria-pressed={active}
      className={cx(
        'inline-flex items-center gap-1.5 rounded-full border px-2.5 py-1 text-xs',
        active
          ? 'border-indigo-300 bg-indigo-50 text-indigo-700 dark:border-indigo-500/40 dark:bg-indigo-500/15 dark:text-indigo-200'
          : 'border-slate-200 text-slate-400 line-through hover:bg-slate-50 dark:border-slate-700 dark:hover:bg-slate-800',
      )}
    >
      {children}
    </button>
  )
}

/**
 * My changes — every open Change Request assigned to or captured by the current user, across all their
 * projects, with the project shown first on each row. Exists to widen attention beyond the projects
 * already top of mind, so the server's order (severity, then least recently updated) is kept as-is
 * rather than grouped by project. Filters are toggles that hide, never reorder.
 */
export function MyChangesView({ actingActorId, projects }: { actingActorId: string; projects: Project[] }) {
  const navigate = useNavigate()
  const { data: changes, isLoading } = useMyChanges()
  const [hiddenProjects, setHiddenProjects] = useState<Set<string>>(() => new Set())
  const [hiddenStatuses, setHiddenStatuses] = useState<Set<ChangeStatus>>(() => new Set())

  const projectById = useMemo(() => new Map(projects.map((p) => [p.id, p])), [projects])

  // Projects present in the result, with counts, in the order they first appear (i.e. most pressing first).
  const projectCounts = useMemo(() => {
    const counts = new Map<string, number>()
    for (const c of changes ?? []) counts.set(c.projectId, (counts.get(c.projectId) ?? 0) + 1)
    return [...counts]
  }, [changes])

  const statusCounts = useMemo(() => {
    const counts = new Map<ChangeStatus, number>()
    for (const c of changes ?? []) counts.set(c.status, (counts.get(c.status) ?? 0) + 1)
    return counts
  }, [changes])

  const visible = (changes ?? []).filter((c) => !hiddenProjects.has(c.projectId) && !hiddenStatuses.has(c.status))

  const toggle = <T,>(set: Set<T>, value: T): Set<T> => {
    const next = new Set(set)
    if (next.has(value)) next.delete(value)
    else next.add(value)
    return next
  }

  if (isLoading) {
    return <div className="text-sm text-slate-400">Loading…</div>
  }

  const total = changes?.length ?? 0

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-4">
      <div className="flex flex-wrap items-baseline gap-2">
        <h1 className="text-lg font-semibold text-slate-800 dark:text-slate-100">My changes</h1>
        <span className="text-sm text-slate-400">
          {visible.length === total ? `${total} open` : `${visible.length} of ${total} open`}
          {projectCounts.length > 0 && ` across ${projectCounts.length} project${projectCounts.length === 1 ? '' : 's'}`}
        </span>
      </div>

      {total > 0 && (
        <div className="flex flex-col gap-2">
          <div className="flex flex-wrap items-center gap-1.5">
            <span className="w-14 text-xs uppercase tracking-wide text-slate-400">Project</span>
            {projectCounts.map(([pid, count]) => {
              const p = projectById.get(pid)
              return (
                <Chip key={pid} active={!hiddenProjects.has(pid)} onClick={() => setHiddenProjects((s) => toggle(s, pid))}>
                  <span>{p?.key ?? p?.name ?? 'Unknown project'}</span>
                  <span className="text-slate-400">{count}</span>
                </Chip>
              )
            })}
          </div>
          <div className="flex flex-wrap items-center gap-1.5">
            <span className="w-14 text-xs uppercase tracking-wide text-slate-400">Status</span>
            {OPEN_STATUSES.map((s) => (
              <Chip key={s} active={!hiddenStatuses.has(s)} onClick={() => setHiddenStatuses((set) => toggle(set, s))}>
                <span>{s}</span>
                <span className="text-slate-400">{statusCounts.get(s) ?? 0}</span>
              </Chip>
            ))}
          </div>
        </div>
      )}

      {visible.length === 0 ? (
        <Card className="p-10 text-center text-sm text-slate-500">
          {total === 0 ? 'Nothing open on your plate.' : 'No changes match the current filters.'}
        </Card>
      ) : (
        <Card className="overflow-hidden">
          <ul className="divide-y divide-slate-100 dark:divide-slate-800">
            {visible.map((c) => {
              const p = projectById.get(c.projectId)
              const assignedToMe = c.assigneeActorId === actingActorId
              return (
                <li key={c.id}>
                  <button
                    onClick={() => navigate(`/projects/${c.projectId}/changes/${c.id}`)}
                    className="flex w-full items-center gap-3 px-3 py-2.5 text-left hover:bg-slate-50 dark:hover:bg-slate-800/60"
                  >
                    <span className="flex w-32 shrink-0 items-center gap-2 sm:w-44" title={p?.name}>
                      <span className={cx('flex h-7 w-7 shrink-0 items-center justify-center rounded-md text-[11px] font-semibold', projectColor(c.projectId))}>
                        {initials(p?.name ?? '?')}
                      </span>
                      <span className="min-w-0 truncate text-sm font-medium text-slate-700 dark:text-slate-200">{p?.name ?? 'Unknown project'}</span>
                    </span>
                    <span className="flex min-w-0 flex-1 flex-col">
                      <span className="flex items-baseline gap-2">
                        {c.displayKey && <span className="shrink-0 font-mono text-xs text-slate-400">{c.displayKey}</span>}
                        <span className="truncate text-sm text-slate-800 dark:text-slate-100">{c.title}</span>
                      </span>
                      <span className="mt-0.5 flex flex-wrap items-center gap-1.5 text-[11px] text-slate-400">
                        <Badge tone={STATUS_TONE[c.status]}>{c.status}</Badge>
                        {c.priority !== 'None' && <Badge tone="amber">{c.priority}</Badge>}
                        <span>{assignedToMe ? 'Assigned to you' : 'Captured by you'}</span>
                      </span>
                    </span>
                    <span className="shrink-0 text-xs text-slate-400" title={`Updated ${new Date(c.updatedAt).toLocaleString()}`}>
                      {relativeTime(c.updatedAt)}
                    </span>
                  </button>
                </li>
              )
            })}
          </ul>
        </Card>
      )}
    </div>
  )
}
