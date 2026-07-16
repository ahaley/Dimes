import { useEffect, useRef, useState } from 'react'
import { useSortable } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import type { ChangeRequest, ChangeStatus, Member } from '../api/types'
import { ALLOWED_TRANSITIONS, STATUS_TONE, initials, kindTone, relativeTime } from '../lifecycle'
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
  epicChildren, expanded, onToggleExpand, onSelectChild, onTransitionChild, onRemoveChild, isDropTarget,
}: {
  change: ChangeRequest
  members: Member[]
  author: string
  onSelect: () => void
  onTransition: (target: ChangeStatus) => void
  // Epic composition (only set for an Epic card): its composed children, expand state, and per-child handlers.
  epicChildren?: ChangeRequest[]
  expanded?: boolean
  onToggleExpand?: () => void
  onSelectChild?: (id: string) => void
  onTransitionChild?: (child: ChangeRequest, target: ChangeStatus) => void
  onRemoveChild?: (child: ChangeRequest) => void
  // True when a dragged request is dwelling on this Epic, armed to be added to its composition.
  isDropTarget?: boolean
}) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: change.id,
    data: { status: change.status },
  })
  const style = { transform: CSS.Transform.toString(transform), transition }
  const [menuOpen, setMenuOpen] = useState(false)

  const assignee = members.find((m) => m.actorId === change.assigneeActorId)
  // Reject/Duplicate need extra input (reason/target) handled in detail; Duplicate is omitted here.
  const menuTargets = ALLOWED_TRANSITIONS[change.status].filter((t) => t !== 'Duplicate')

  return (
    <div
      ref={setNodeRef}
      style={style}
      {...attributes}
      {...listeners}
      onClick={(e) => {
        // A press on the ⋯ menu (button, dropdown, or its backdrop) must never open the card. Guarding
        // on the target here means navigation can't slip through if event propagation is reordered by
        // the drag sensors during a touch/synthetic input sequence.
        if ((e.target as HTMLElement).closest('[data-card-menu]')) return
        onSelect()
      }}
      className={cx(
        'group relative cursor-grab overflow-hidden rounded-md border border-slate-200 bg-white active:cursor-grabbing dark:border-slate-700 dark:bg-slate-800',
        'hover:border-indigo-300 hover:shadow-sm',
        isDragging && 'opacity-40',
        isDropTarget && 'border-indigo-500 ring-2 ring-indigo-400',
      )}
    >
      <div className={cx('h-0.5 w-full', TONE_RULE[STATUS_TONE[change.status]] ?? 'bg-slate-300')} />
      {isDropTarget && (
        <div className="bg-indigo-500 px-2 py-1 text-center text-[11px] font-semibold text-white">
          Release to add to Epic
        </div>
      )}
      {change.workOrderStatus === 'Blocked' && (
        <div className="bg-amber-500 px-2 py-1 text-center text-[11px] font-semibold text-white">
          Agent reported blocked
        </div>
      )}
      <div className="p-2.5">
        <div className="flex items-start justify-between gap-1">
          <p className="text-sm font-medium leading-snug text-slate-800 dark:text-slate-100">{change.title}</p>
          {menuTargets.length > 0 && (
            <button
              aria-label="Change status"
              data-card-menu
              // Stop the drag sensors (mouse + touch) from treating a press on the menu as the start of
              // a card drag — listeners live on the card root, so the event must not bubble there.
              onMouseDown={(e) => e.stopPropagation()}
              onTouchStart={(e) => e.stopPropagation()}
              onClick={(e) => { e.stopPropagation(); setMenuOpen((v) => !v) }}
              className="-my-1 -mr-1 shrink-0 rounded p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-600 dark:hover:bg-slate-700 dark:hover:text-slate-200"
            >
              ⋯
            </button>
          )}
        </div>

        <div className="mt-2 flex flex-wrap items-center gap-1.5">
          {change.displayKey && (
            <span className="font-mono text-[11px] text-slate-400">{change.displayKey}</span>
          )}
          {change.kind === 'ObservationDriven' && (
            <span title="Promoted from an observation" className="inline-block h-1.5 w-1.5 rounded-full bg-amber-400" />
          )}
          <Badge tone={kindTone(change.kind)}>{change.kind}</Badge>
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

        {/* An agent has reported this change done. The prompt is derived, not stored, so it clears itself
            however the human acts — here, via the ⋯ menu, or by dragging the card to In Review. */}
        {change.workOrderStatus === 'Reported' && change.status === 'InDevelopment' && (
          <button
            data-card-menu
            // Same drag/navigation guards as the ⋯ menu: without data-card-menu the click falls through
            // the root's onClick and opens the detail modal, and without the pointer stops a press here
            // starts a card drag.
            onMouseDown={(e) => e.stopPropagation()}
            onTouchStart={(e) => e.stopPropagation()}
            onClick={(e) => { e.stopPropagation(); onTransition('InReview') }}
            className="mt-2 flex w-full items-center justify-center gap-1 rounded bg-indigo-600 px-2 py-1 text-[11px] font-semibold text-white hover:bg-indigo-500"
          >
            Agent reports done — move to In Review
          </button>
        )}

        {/* Epic composition: a toggle to reveal the composed children nested in place. The nesting is the
            persistent cue that these changes belong to this Epic. */}
        {change.kind === 'Epic' && epicChildren && epicChildren.length > 0 && (
          <div className="mt-2">
            <button
              onPointerDown={(e) => e.stopPropagation()}
              onClick={(e) => { e.stopPropagation(); onToggleExpand?.() }}
              className="flex w-full items-center gap-1.5 text-left text-[11px] font-medium text-slate-500 hover:text-slate-700 dark:hover:text-slate-300"
            >
              <span>{expanded ? '▾' : '▸'}</span>
              {epicChildren.length} composed {epicChildren.length === 1 ? 'change' : 'changes'}
            </button>
            {expanded && (
              <div className="mt-1.5 space-y-1 border-l-2 border-indigo-200 pl-2 dark:border-indigo-500/40">
                {epicChildren.map((child) => (
                  <NestedChild
                    key={child.id}
                    child={child}
                    members={members}
                    onSelect={() => onSelectChild?.(child.id)}
                    onTransition={(target) => onTransitionChild?.(child, target)}
                    onRemove={() => onRemoveChild?.(child)}
                  />
                ))}
              </div>
            )}
          </div>
        )}
      </div>

      {menuOpen && (
        <>
          <div
            data-card-menu
            className="fixed inset-0 z-10"
            onClick={(e) => { e.stopPropagation(); setMenuOpen(false) }}
            onMouseDown={(e) => e.stopPropagation()}
            onTouchStart={(e) => e.stopPropagation()}
          />
          <div
            data-card-menu
            className="absolute right-2 top-8 z-20 w-44 max-w-[calc(100vw-2rem)] overflow-hidden rounded-md border border-slate-200 bg-white py-1 shadow-lg dark:border-slate-700 dark:bg-slate-800"
            onClick={(e) => e.stopPropagation()}
            onMouseDown={(e) => e.stopPropagation()}
            onTouchStart={(e) => e.stopPropagation()}
          >
            {menuTargets.map((t) => (
              <button
                key={t}
                onClick={() => { setMenuOpen(false); onTransition(t) }}
                className={cx(
                  'block w-full px-3 py-2.5 text-left text-sm hover:bg-slate-50 dark:hover:bg-slate-700',
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

/** A composed child rendered nested inside its Epic card: a compact mini-card showing its title, a short
 * description preview, and a meta footer (status / priority / assignee / age) plus a transition menu.
 * Children are moved here or in the detail — they don't have their own board column. */
function NestedChild({
  child, members, onSelect, onTransition, onRemove,
}: {
  child: ChangeRequest
  members: Member[]
  onSelect: () => void
  onTransition: (target: ChangeStatus) => void
  onRemove: () => void
}) {
  const [menuOpen, setMenuOpen] = useState(false)
  // Press-and-hold reveals the "Remove from Epic" action. A quick click still opens the child detail;
  // only a ~500ms hold arms removal. The timer is cancelled if the pointer moves (a scroll, not a hold).
  const [armed, setArmed] = useState(false)
  const pressTimer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)
  const startPos = useRef<{ x: number; y: number } | null>(null)
  const suppressClick = useRef(false)
  const clearPress = () => { if (pressTimer.current) clearTimeout(pressTimer.current); pressTimer.current = undefined }
  // Cancel a pending long-press if the child unmounts mid-hold (e.g. a board refetch), so the timer can't
  // fire setState on an unmounted component.
  useEffect(() => () => { if (pressTimer.current) clearTimeout(pressTimer.current) }, [])
  const onPointerDown = (e: React.PointerEvent) => {
    e.stopPropagation() // never start the Epic card's drag
    if (armed) return
    startPos.current = { x: e.clientX, y: e.clientY }
    clearPress()
    pressTimer.current = setTimeout(() => { suppressClick.current = true; setMenuOpen(false); setArmed(true) }, 500)
  }
  const onPointerMove = (e: React.PointerEvent) => {
    if (!pressTimer.current || !startPos.current) return
    if (Math.hypot(e.clientX - startPos.current.x, e.clientY - startPos.current.y) > 8) clearPress()
  }
  const onClick = (e: React.MouseEvent) => {
    e.stopPropagation()
    if (suppressClick.current) { suppressClick.current = false; return } // the hold just opened the action
    if (armed) { setArmed(false); return } // a click cancels the armed state
    onSelect()
  }
  const menuTargets = ALLOWED_TRANSITIONS[child.status].filter((t) => t !== 'Duplicate')
  const assignee = members.find((m) => m.actorId === child.assigneeActorId)
  return (
    <div className="relative">
      {armed && <div className="fixed inset-0 z-10" onClick={(e) => { e.stopPropagation(); setArmed(false) }} />}
      <div
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={clearPress}
        onPointerLeave={clearPress}
        onClick={onClick}
        className={cx(
          'cursor-pointer space-y-1 rounded border border-slate-200 bg-slate-50 p-2 hover:border-indigo-300 dark:border-slate-700 dark:bg-slate-900/40',
          armed && 'relative z-20 border-indigo-400 ring-2 ring-indigo-400',
        )}
      >
        <div className="flex items-start gap-1.5">
          {child.displayKey && <span className="shrink-0 font-mono text-[10px] leading-snug text-slate-400">{child.displayKey}</span>}
          <span className="min-w-0 flex-1 text-xs font-medium leading-snug text-slate-700 dark:text-slate-200">{child.title}</span>
          {menuTargets.length > 0 && (
            <button
              aria-label="Change status"
              onPointerDown={(e) => e.stopPropagation()}
              onClick={(e) => { e.stopPropagation(); setArmed(false); setMenuOpen((v) => !v) }}
              className="-mt-0.5 shrink-0 rounded px-1 text-slate-400 hover:bg-slate-100 hover:text-slate-600 dark:hover:bg-slate-700 dark:hover:text-slate-200"
            >
              ⋯
            </button>
          )}
        </div>

        {child.description && (
          <p className="line-clamp-3 text-[11px] leading-snug text-slate-500 dark:text-slate-400">{child.description}</p>
        )}

        <div className="flex flex-wrap items-center gap-1.5">
          <Badge tone={STATUS_TONE[child.status]}>{child.status}</Badge>
          {child.priority !== 'None' && <Badge tone="amber">{child.priority}</Badge>}
          <span className="ml-auto flex items-center gap-1.5 text-[10px] text-slate-400">
            <span>{relativeTime(child.updatedAt)}</span>
            {assignee && (
              <span
                title={assignee.displayName}
                className="inline-flex h-4 w-4 items-center justify-center rounded-full bg-slate-200 text-[9px] font-semibold text-slate-600 dark:bg-slate-700 dark:text-slate-200"
              >
                {initials(assignee.displayName)}
              </span>
            )}
          </span>
        </div>

        {armed && (
          <button
            onPointerDown={(e) => e.stopPropagation()}
            onClick={(e) => { e.stopPropagation(); setArmed(false); onRemove() }}
            className="mt-1 flex w-full items-center justify-center gap-1 rounded bg-red-600 px-2 py-1 text-[11px] font-semibold text-white hover:bg-red-500"
          >
            ✕ Remove from Epic
          </button>
        )}
      </div>
      {menuOpen && (
        <>
          <div className="fixed inset-0 z-10" onClick={(e) => { e.stopPropagation(); setMenuOpen(false) }} />
          <div
            className="absolute right-1 top-7 z-20 w-40 overflow-hidden rounded-md border border-slate-200 bg-white py-1 shadow-lg dark:border-slate-700 dark:bg-slate-800"
            onClick={(e) => e.stopPropagation()}
            onPointerDown={(e) => e.stopPropagation()}
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
