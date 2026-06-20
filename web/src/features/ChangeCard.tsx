import { useState } from 'react'
import { useDraggable } from '@dnd-kit/core'
import type { ChangeRequest, ChangeStatus, Member } from '../api/types'
import { ALLOWED_TRANSITIONS, STATUS_TONE, initials, relativeTime } from '../lifecycle'
import { Badge, cx } from '../components/ui'

const TONE_RULE: Record<string, string> = {
  slate: 'bg-slate-300',
  amber: 'bg-amber-400',
  violet: 'bg-violet-400',
  indigo: 'bg-indigo-400',
  green: 'bg-green-400',
  red: 'bg-red-400',
}

export function ChangeCard({
  change, members, author, onSelect, onTransition,
}: {
  change: ChangeRequest
  members: Member[]
  author: string
  onSelect: () => void
  onTransition: (target: ChangeStatus) => void
}) {
  const { attributes, listeners, setNodeRef, isDragging } = useDraggable({
    id: change.id,
    data: { status: change.status },
  })
  const [menuOpen, setMenuOpen] = useState(false)

  const assignee = members.find((m) => m.actorId === change.assigneeActorId)
  // Reject/Duplicate need extra input (reason/target) handled in detail; Duplicate is omitted here.
  const menuTargets = ALLOWED_TRANSITIONS[change.status].filter((t) => t !== 'Duplicate')

  return (
    <div
      ref={setNodeRef}
      {...attributes}
      {...listeners}
      onClick={onSelect}
      className={cx(
        'group relative cursor-grab overflow-hidden rounded-md border border-slate-200 bg-white active:cursor-grabbing dark:border-slate-700 dark:bg-slate-800',
        'hover:border-indigo-300 hover:shadow-sm',
        isDragging && 'opacity-40',
      )}
    >
      <div className={cx('h-0.5 w-full', TONE_RULE[STATUS_TONE[change.status]] ?? 'bg-slate-300')} />
      <div className="p-2.5">
        <div className="flex items-start justify-between gap-1">
          <p className="text-sm font-medium leading-snug text-slate-800 dark:text-slate-100">{change.title}</p>
          {menuTargets.length > 0 && (
            <button
              aria-label="Change status"
              onPointerDown={(e) => e.stopPropagation()}
              onClick={(e) => { e.stopPropagation(); setMenuOpen((v) => !v) }}
              className="shrink-0 rounded px-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600 dark:hover:bg-slate-700 dark:hover:text-slate-200"
            >
              ⋯
            </button>
          )}
        </div>

        <div className="mt-2 flex flex-wrap items-center gap-1.5">
          {change.kind === 'ObservationDriven' && (
            <span title="Promoted from an observation" className="inline-block h-1.5 w-1.5 rounded-full bg-amber-400" />
          )}
          <Badge tone="slate">{change.kind}</Badge>
          {change.priority !== 'None' && <Badge tone="amber">{change.priority}</Badge>}
          <span className="ml-auto flex items-center gap-1.5 text-[11px] text-slate-400">
            <span>{relativeTime(change.updatedAt)}</span>
            {assignee && (
              <span
                title={assignee.displayName}
                className="inline-flex h-5 w-5 items-center justify-center rounded-full bg-slate-200 text-[10px] font-semibold text-slate-600 dark:bg-slate-700 dark:text-slate-200"
              >
                {initials(assignee.displayName)}
              </span>
            )}
          </span>
        </div>

        <div className="mt-1.5 flex items-center gap-1 text-[11px] text-slate-400" title={`Author: ${author}`}>
          <span className="inline-flex h-4 w-4 items-center justify-center rounded-full bg-slate-200 text-[9px] font-semibold text-slate-600 dark:bg-slate-700 dark:text-slate-200">
            {initials(author)}
          </span>
          <span className="truncate">by {author}</span>
        </div>
      </div>

      {menuOpen && (
        <>
          <div className="fixed inset-0 z-10" onClick={(e) => { e.stopPropagation(); setMenuOpen(false) }} />
          <div
            className="absolute right-2 top-8 z-20 w-40 overflow-hidden rounded-md border border-slate-200 bg-white py-1 shadow-lg dark:border-slate-700 dark:bg-slate-800"
            onClick={(e) => e.stopPropagation()}
          >
            {menuTargets.map((t) => (
              <button
                key={t}
                onClick={() => { setMenuOpen(false); onTransition(t) }}
                className={cx(
                  'block w-full px-3 py-1.5 text-left text-sm hover:bg-slate-50 dark:hover:bg-slate-700',
                  t === 'Rejected' ? 'text-red-600 dark:text-red-400' : 'text-slate-700 dark:text-slate-200',
                )}
              >
                Move to {t}
              </button>
            ))}
          </div>
        </>
      )}
    </div>
  )
}
