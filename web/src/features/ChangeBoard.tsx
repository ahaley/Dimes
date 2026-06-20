import { useMemo, useState } from 'react'
import { DndContext, PointerSensor, useDroppable, useSensor, useSensors, type DragEndEvent } from '@dnd-kit/core'
import { useActors, useChanges, useTransition } from '../api/hooks'
import { LIFECYCLE_COLUMNS, type ChangeRequest, type ChangeStatus, type Member } from '../api/types'
import { STATUS_TONE } from '../lifecycle'
import { Badge, cx } from '../components/ui'
import { useToast } from '../components/Toast'
import { ChangeCard } from './ChangeCard'

export function ChangeBoard({
  projectId, members, onSelect,
}: { projectId: string; members: Member[]; onSelect: (id: string) => void }) {
  const { data: changes } = useChanges(projectId)
  const transition = useTransition(projectId)
  const toast = useToast()
  const [closedOpen, setClosedOpen] = useState(false)

  // Resolve an author's name from members plus all actors (incl. archived/removed) so authors who
  // are no longer members still show.
  const { data: actors } = useActors(false, true)
  const nameById = useMemo(() => {
    const m = new Map<string, string>()
    for (const a of actors ?? []) m.set(a.id, a.displayName)
    for (const mem of members) m.set(mem.actorId, mem.displayName)
    return m
  }, [actors, members])
  const authorOf = (c: ChangeRequest) => nameById.get(c.createdByActorId) ?? 'Unknown'

  const sensors = useSensors(useSensor(PointerSensor, { activationConstraint: { distance: 5 } }))

  const byStatus = (status: ChangeStatus) => (changes ?? []).filter((c) => c.status === status)
  const terminal = (changes ?? []).filter((c) => c.status === 'Rejected' || c.status === 'Duplicate')

  const requestTransition = (change: ChangeRequest, target: ChangeStatus) => {
    if (target === change.status) return
    transition.mutate(
      { id: change.id, target },
      {
        onSuccess: () => toast.success(`Moved “${change.title}” to ${target}`),
        onError: (e) => toast.error(e instanceof Error ? e.message : 'Transition failed'),
      },
    )
  }

  const onDragEnd = (e: DragEndEvent) => {
    const target = e.over?.id as ChangeStatus | undefined
    if (!target) return
    const change = (changes ?? []).find((c) => c.id === e.active.id)
    if (change) requestTransition(change, target)
  }

  return (
    <DndContext sensors={sensors} onDragEnd={onDragEnd}>
      <div className="flex gap-3 overflow-x-auto pb-3">
        {LIFECYCLE_COLUMNS.map((status) => (
          <div key={status} className="flex">
            {status === 'Approved' && <Gate />}
            <Column status={status} changes={byStatus(status)} members={members} authorOf={authorOf} onSelect={onSelect} onTransition={requestTransition} />
          </div>
        ))}
      </div>

      {terminal.length > 0 && (
        <div className="mt-3 rounded-lg border border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900">
          <button
            onClick={() => setClosedOpen((v) => !v)}
            className="flex w-full items-center gap-2 px-3 py-2 text-left text-xs font-semibold uppercase tracking-wide text-slate-500"
          >
            <span>{closedOpen ? '▾' : '▸'}</span> Closed <Badge tone="slate">{terminal.length}</Badge>
          </button>
          {closedOpen && (
            <ul className="space-y-1 px-3 pb-3">
              {terminal.map((c) => (
                <li key={c.id}>
                  <button onClick={() => onSelect(c.id)} className="text-sm text-slate-500 hover:text-slate-800 dark:hover:text-slate-200">
                    {c.title} <span className="text-slate-400">· {c.status}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </DndContext>
  )
}

function Column({
  status, changes, members, authorOf, onSelect, onTransition,
}: {
  status: ChangeStatus
  changes: ChangeRequest[]
  members: Member[]
  authorOf: (change: ChangeRequest) => string
  onSelect: (id: string) => void
  onTransition: (change: ChangeRequest, target: ChangeStatus) => void
}) {
  const { setNodeRef, isOver } = useDroppable({ id: status })
  return (
    <div
      ref={setNodeRef}
      className={cx(
        'flex w-64 shrink-0 flex-col rounded-lg border bg-slate-50/70 p-2 transition-colors dark:bg-slate-800/40',
        isOver ? 'border-indigo-400 bg-indigo-50/60' : 'border-transparent',
      )}
    >
      <div className="mb-2 flex items-center justify-between px-1">
        <span className="text-xs font-semibold uppercase tracking-wide text-slate-500">{status}</span>
        <Badge tone={STATUS_TONE[status]}>{changes.length}</Badge>
      </div>
      <div className="space-y-2">
        {changes.map((c) => (
          <ChangeCard
            key={c.id}
            change={c}
            members={members}
            author={authorOf(c)}
            onSelect={() => onSelect(c.id)}
            onTransition={(target) => onTransition(c, target)}
          />
        ))}
        {changes.length === 0 && (
          <p className="rounded-md border border-dashed border-slate-200 px-2 py-6 text-center text-xs text-slate-300 dark:border-slate-700 dark:text-slate-600">
            Drop here
          </p>
        )}
      </div>
    </div>
  )
}

/** The whitelist gate — the signature divider between candidate columns and committed work. */
function Gate() {
  return (
    <div className="mr-3 flex w-8 shrink-0 flex-col items-center justify-center" title="Whitelist gate — Maintainer approval required">
      <div className="flex-1 border-l border-dashed border-violet-300" />
      <span className="my-2 select-none rounded bg-violet-100 px-1 py-0.5 text-[10px] font-semibold text-violet-700">🔒</span>
      <div className="flex-1 border-l border-dashed border-violet-300" />
    </div>
  )
}
