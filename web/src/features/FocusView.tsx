import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useChanges } from '../api/hooks'
import type { ChangeStatus, Member } from '../api/types'
import { STATUS_TONE, initials, relativeTime } from '../lifecycle'
import { Badge, Button, Card, cx } from '../components/ui'
import { ChangeDetailBody } from './ChangeDetail'

/**
 * Focus Mode — a full-page, low-chrome queue over a single board state. A developer clearing
 * InDevelopment, or a reviewer validating InReview, works through that column's changes one at a
 * time: a vertical queue rail (left) plus the full change detail (right) with its advance action.
 * Advancing a change drops it from the queue and auto-selects the next one.
 */
export function FocusView({ actingActorId, members }: { actingActorId: string; members: Member[] }) {
  const { projectId = '', status = '' } = useParams()
  const focusStatus = status as ChangeStatus
  const navigate = useNavigate()
  const { data: changes } = useChanges(projectId)

  const [railOpen, setRailOpen] = useState(true)
  const [selectedId, setSelectedId] = useState<string>()
  const lastIndexRef = useRef(0)

  // Oldest-updated first, so the backlog is worked top-down.
  const queue = useMemo(
    () => (changes ?? [])
      .filter((c) => c.status === focusStatus)
      .sort((a, b) => a.updatedAt.localeCompare(b.updatedAt)),
    [changes, focusStatus],
  )

  // Keep a valid selection. When the selected change leaves the queue (advanced to the next status),
  // the item that shifted into its index becomes selected — i.e. auto-advance to the next; or the
  // last item if it was at the end.
  useEffect(() => {
    if (queue.length === 0) {
      if (selectedId !== undefined) setSelectedId(undefined)
      return
    }
    const idx = queue.findIndex((c) => c.id === selectedId)
    if (idx === -1) {
      const next = Math.min(lastIndexRef.current, queue.length - 1)
      setSelectedId(queue[next].id)
    } else {
      lastIndexRef.current = idx
    }
  }, [queue, selectedId])

  const currentIndex = queue.findIndex((c) => c.id === selectedId)
  const selected = currentIndex >= 0 ? queue[currentIndex] : undefined
  const goTo = (i: number) => { if (i >= 0 && i < queue.length) setSelectedId(queue[i].id) }

  // Arrow-key navigation, unless the user is typing in the detail pane (comment box, edit fields).
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const el = document.activeElement
      const typing = el instanceof HTMLElement
        && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.tagName === 'SELECT' || el.isContentEditable)
      if (typing) return
      if (e.key === 'ArrowDown' || e.key === 'ArrowRight') { e.preventDefault(); goTo(currentIndex + 1) }
      if (e.key === 'ArrowUp' || e.key === 'ArrowLeft') { e.preventDefault(); goTo(currentIndex - 1) }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [currentIndex, queue]) // eslint-disable-line react-hooks/exhaustive-deps

  const exit = () => navigate(`/projects/${projectId}`)

  return (
    <div className="flex h-full min-h-0 flex-col gap-3">
      {/* Header */}
      <div className="flex items-center gap-2">
        <Badge tone={STATUS_TONE[focusStatus] ?? 'slate'}>{focusStatus}</Badge>
        <span className="text-sm font-semibold uppercase tracking-wide text-slate-500">Focus</span>
        {queue.length > 0 && (
          <span className="text-sm text-slate-400">{Math.max(currentIndex, 0) + 1} of {queue.length}</span>
        )}
        <span className="ml-auto flex items-center gap-2">
          <Button variant="subtle" onClick={() => setRailOpen((v) => !v)}>
            {railOpen ? 'Hide queue' : 'Show queue'}
          </Button>
          <Button variant="subtle" onClick={exit}>Exit</Button>
        </span>
      </div>

      {queue.length === 0 ? (
        <Card className="flex flex-1 flex-col items-center justify-center gap-3 p-10 text-center">
          <p className="text-sm text-slate-500">Nothing in {focusStatus}.</p>
          <Button variant="default" onClick={exit}>Back to board</Button>
        </Card>
      ) : (
        <div className="flex min-h-0 flex-1 gap-3">
          {/* Queue rail */}
          {railOpen && (
            <Card className="w-80 shrink-0 overflow-auto p-1.5">
              <ul className="space-y-1">
                {queue.map((c, i) => {
                  const assignee = members.find((m) => m.actorId === c.assigneeActorId)
                  return (
                    <li key={c.id}>
                      <button
                        onClick={() => setSelectedId(c.id)}
                        className={cx(
                          'w-full rounded-md px-2 py-2 text-left',
                          c.id === selectedId
                            ? 'bg-indigo-50 ring-1 ring-indigo-200 dark:bg-indigo-500/10 dark:ring-indigo-500/30'
                            : 'hover:bg-slate-50 dark:hover:bg-slate-800',
                        )}
                      >
                        <div className="flex items-start gap-1.5">
                          <span className="mt-0.5 w-4 shrink-0 text-[11px] text-slate-400">{i + 1}</span>
                          <span className="min-w-0 flex-1 line-clamp-2 text-sm text-slate-800 dark:text-slate-100">{c.title}</span>
                        </div>
                        <div className="mt-1 flex items-center gap-1.5 pl-5 text-[11px] text-slate-400">
                          {c.priority !== 'None' && <Badge tone="amber">{c.priority}</Badge>}
                          <span className="ml-auto">{relativeTime(c.updatedAt)}</span>
                          {assignee && (
                            <span
                              title={assignee.displayName}
                              className="inline-flex h-4 w-4 items-center justify-center rounded-full bg-slate-200 text-[9px] font-semibold text-slate-600 dark:bg-slate-700 dark:text-slate-200"
                            >
                              {initials(assignee.displayName)}
                            </span>
                          )}
                        </div>
                      </button>
                    </li>
                  )
                })}
              </ul>
            </Card>
          )}

          {/* Detail pane */}
          <Card className="flex min-h-0 flex-1 flex-col overflow-hidden">
            <div className="flex shrink-0 items-center gap-2 border-b border-slate-200 px-4 py-2 dark:border-slate-800">
              <Button variant="subtle" disabled={currentIndex <= 0} onClick={() => goTo(currentIndex - 1)}>← Prev</Button>
              <Button variant="subtle" disabled={currentIndex >= queue.length - 1} onClick={() => goTo(currentIndex + 1)}>Next →</Button>
              <span className="ml-auto text-xs text-slate-400">Advance with the status buttons below</span>
            </div>
            <div className="min-h-0 flex-1 overflow-auto p-4">
              {selected && (
                <ChangeDetailBody
                  key={selected.id}
                  changeId={selected.id}
                  projectId={projectId}
                  actingActorId={actingActorId}
                  members={members}
                />
              )}
            </div>
          </Card>
        </div>
      )}
    </div>
  )
}
