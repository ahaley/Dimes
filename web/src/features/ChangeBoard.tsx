import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { DndContext, pointerWithin, rectIntersection, useDroppable, type CollisionDetection, type DragEndEvent } from '@dnd-kit/core'
import { SortableContext, arrayMove, verticalListSortingStrategy } from '@dnd-kit/sortable'
import { useActors, useChanges, useReorderChanges, useTransition } from '../api/hooks'
import { LIFECYCLE_COLUMNS, type ChangeRequest, type ChangeStatus, type Member } from '../api/types'
import { STATUS_TONE, relativeTime } from '../lifecycle'
import { Badge, cx } from '../components/ui'
import { useToast } from '../components/Toast'
import { useBoardSensors } from '../lib/dndSensors'
import { useIsDesktop } from '../hooks/useMediaQuery'
import { ChangeCard } from './ChangeCard'

// The Done column keeps recently-accepted Change Requests visible and collapses older ones so it can't
// grow without bound. "Recent" = accepted within this window (by CompletedAt); a Done change with no
// CompletedAt (e.g. pre-feature, before the startup backfill stamps it) counts as older.
const DONE_RECENT_DAYS = 14
function isRecentlyAccepted(c: ChangeRequest): boolean {
  return c.completedAt != null && Date.now() - new Date(c.completedAt).getTime() < DONE_RECENT_DAYS * 864e5
}

// Real-time board search. An empty query matches everything; otherwise match the change's title or
// description (case-insensitive).
function matchesQuery(c: ChangeRequest, query: string): boolean {
  const q = query.trim().toLowerCase()
  if (q === '') return true
  return c.title.toLowerCase().includes(q) || (c.description?.toLowerCase().includes(q) ?? false)
}

// Resolve the drop target from the pointer position — the column or card actually under the cursor —
// not the dragged card's geometric center. closestCenter measures the dragged rect against every
// droppable, so it can latch onto a card in an adjacent column and fire an illegal cross-column
// transition (e.g. dragging toward InReview resolving to a Done card). Fall back to rect intersection
// only when the pointer is outside every droppable (fast drags / auto-scroll).
const boardCollisionDetection: CollisionDetection = (args) => {
  const byPointer = pointerWithin(args)
  return byPointer.length > 0 ? byPointer : rectIntersection(args)
}

export function ChangeBoard({
  projectId, members, query, mineOnly, actingActorId, onSelect,
}: { projectId: string; members: Member[]; query: string; mineOnly: boolean; actingActorId: string; onSelect: (id: string) => void }) {
  const { data: changes } = useChanges(projectId)
  const transition = useTransition(projectId)
  const reorder = useReorderChanges(projectId)
  const toast = useToast()
  const navigate = useNavigate()
  const [closedOpen, setClosedOpen] = useState(false)

  // Below md the board shows one stage at a time (a switcher picks which); at md+ it's the full
  // horizontal column row. `activeStatus` is the stage shown on mobile.
  const isDesktop = useIsDesktop()
  const [activeStatus, setActiveStatus] = useState<ChangeStatus>(LIFECYCLE_COLUMNS[0])

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

  const sensors = useBoardSensors()

  // Apply the board filters before splitting into columns so every column (and the Closed section)
  // reflects them: the text query AND, when the "assigned to me" toggle is on, the current actor.
  const visible = (changes ?? []).filter(
    (c) => matchesQuery(c, query) && (!mineOnly || c.assigneeActorId === actingActorId),
  )

  // Mirror the server order (OrderBy SortOrder, then UpdatedAt desc) so an optimistic SortOrder change
  // from a drag reorders the column immediately — otherwise the card snaps back until the refetch lands.
  const byStatus = (status: ChangeStatus) =>
    visible
      .filter((c) => c.status === status)
      .sort((a, b) => a.sortOrder - b.sortOrder || b.updatedAt.localeCompare(a.updatedAt))
  const terminal = visible.filter((c) => c.status === 'Rejected' || c.status === 'Duplicate')

  // Count for a column header / mobile switcher chip — mirrors what the column actually shows (Done
  // displays only recently-accepted items; older ones are collapsed below a disclosure).
  const countFor = (status: ChangeStatus) =>
    status === 'Done' ? byStatus('Done').filter(isRecentlyAccepted).length : byStatus(status).length

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

  const isStatusId = (id: string): id is ChangeStatus =>
    (LIFECYCLE_COLUMNS as string[]).includes(id)

  const onDragEnd = (e: DragEndEvent) => {
    const overId = e.over?.id as string | undefined
    if (!overId) return
    const change = (changes ?? []).find((c) => c.id === e.active.id)
    if (!change) return

    // Dropped on a column (its empty area) → cross-column move to that status.
    if (isStatusId(overId)) {
      requestTransition(change, overId)
      return
    }

    // Dropped on another card.
    const overChange = (changes ?? []).find((c) => c.id === overId)
    if (!overChange || overChange.id === change.id) return

    if (overChange.status !== change.status) {
      // Card landed in a different column → transition into that column.
      requestTransition(change, overChange.status)
      return
    }

    // Same column → persist the new manual order.
    const columnIds = byStatus(change.status).map((c) => c.id)
    const from = columnIds.indexOf(change.id)
    const to = columnIds.indexOf(overChange.id)
    if (from === -1 || to === -1 || from === to) return
    reorder.mutate(
      { status: change.status, orderedIds: arrayMove(columnIds, from, to) },
      { onError: (err) => toast.error(err instanceof Error ? err.message : 'Reorder failed') },
    )
  }

  // Build a column for a given stage, deriving the Done recent/older split. `mobile` switches the
  // column to full width (it's the only one on screen) and surfaces normally-hover-only affordances.
  const renderColumn = (status: ChangeStatus, mobile: boolean) => {
    const inColumn = byStatus(status)
    const recent = status === 'Done' ? inColumn.filter(isRecentlyAccepted) : inColumn
    const older = status === 'Done' ? inColumn.filter((c) => !isRecentlyAccepted(c)) : undefined
    return (
      <Column
        status={status}
        changes={recent}
        olderItems={older}
        members={members}
        authorOf={authorOf}
        onSelect={onSelect}
        onTransition={requestTransition}
        onFocus={() => navigate(`/projects/${projectId}/focus/${status}`)}
        mobile={mobile}
      />
    )
  }

  // A quick horizontal swipe on the mobile column body steps to the adjacent stage. We require a clear
  // horizontal intent (>50px and dominant over vertical) so vertical scrolls and long-press card drags
  // don't trigger a stage change.
  // A long-press card drag also ends in a touchend on this wrapper (dnd-kit's TouchSensor preventDefaults
  // but doesn't stopPropagation), so without this flag a horizontal-ish drag would also step the stage.
  // onDragStart sets it; each fresh touch clears it, so only a genuine swipe (no drag) switches stages.
  const swipeStart = useRef<{ x: number; y: number } | null>(null)
  const draggingRef = useRef(false)
  const onColumnTouchStart = (e: React.TouchEvent) => {
    const t = e.touches[0]
    draggingRef.current = false
    swipeStart.current = { x: t.clientX, y: t.clientY }
  }
  const onColumnTouchEnd = (e: React.TouchEvent) => {
    const start = swipeStart.current
    swipeStart.current = null
    if (!start || draggingRef.current) return
    const t = e.changedTouches[0]
    const dx = t.clientX - start.x
    const dy = t.clientY - start.y
    if (Math.abs(dx) < 50 || Math.abs(dx) < Math.abs(dy) * 1.5) return
    const idx = LIFECYCLE_COLUMNS.indexOf(activeStatus)
    const next = dx < 0 ? idx + 1 : idx - 1
    if (next >= 0 && next < LIFECYCLE_COLUMNS.length) setActiveStatus(LIFECYCLE_COLUMNS[next])
  }

  return (
    <DndContext sensors={sensors} collisionDetection={boardCollisionDetection} onDragStart={() => { draggingRef.current = true }} onDragEnd={onDragEnd}>
      {isDesktop ? (
        <div className="flex gap-3 overflow-x-auto pb-3">
          {LIFECYCLE_COLUMNS.map((status) => (
            <div key={status} className="flex">
              {status === 'Approved' && <Gate />}
              {renderColumn(status, false)}
            </div>
          ))}
        </div>
      ) : (
        <div className="space-y-3">
          <MobileStageSwitcher active={activeStatus} onSelect={setActiveStatus} countFor={countFor} />
          <div onTouchStart={onColumnTouchStart} onTouchEnd={onColumnTouchEnd}>
            {renderColumn(activeStatus, true)}
          </div>
        </div>
      )}

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
                    {c.displayKey && <span className="font-mono text-xs text-slate-400">{c.displayKey} </span>}
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
  status, changes, olderItems, members, authorOf, onSelect, onTransition, onFocus, mobile,
}: {
  status: ChangeStatus
  changes: ChangeRequest[]
  olderItems?: ChangeRequest[]
  members: Member[]
  authorOf: (change: ChangeRequest) => string
  onSelect: (id: string) => void
  onTransition: (change: ChangeRequest, target: ChangeStatus) => void
  onFocus: () => void
  mobile?: boolean
}) {
  const { setNodeRef, isOver } = useDroppable({ id: status })
  const [showOlder, setShowOlder] = useState(false)
  // Focus mode works through a column one by one. Done is focusable too (for late review of accepted
  // Change Requests), where it gains a date-range filter — see FocusView.
  const canFocus = true
  return (
    <div
      ref={setNodeRef}
      className={cx(
        'group/col flex flex-col rounded-lg border bg-slate-50/70 p-2 transition-colors dark:bg-slate-800/40',
        mobile ? 'w-full' : 'w-64 shrink-0',
        isOver ? 'border-indigo-400 bg-indigo-50/60' : 'border-transparent',
      )}
    >
      <div className="mb-2 flex items-center gap-1 px-1">
        <span className="text-xs font-semibold uppercase tracking-wide text-slate-500">{status}</span>
        <Badge tone={STATUS_TONE[status]}>{changes.length}</Badge>
        {canFocus && (
          <button
            onClick={onFocus}
            title={`Focus ${status} — work through one by one`}
            aria-label={`Focus ${status}`}
            className={cx(
              'ml-auto rounded text-slate-400 transition-opacity hover:bg-slate-200 hover:text-slate-600 focus:opacity-100 dark:hover:bg-slate-700 dark:hover:text-slate-200',
              mobile ? 'p-1.5 opacity-100' : 'p-0.5 opacity-0 group-hover/col:opacity-100',
            )}
          >
            ⤢
          </button>
        )}
      </div>
      <div className="space-y-2">
        <SortableContext items={changes.map((c) => c.id)} strategy={verticalListSortingStrategy}>
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
        </SortableContext>
        {changes.length === 0 && !olderItems?.length && (
          <p className="rounded-md border border-dashed border-slate-200 px-2 py-6 text-center text-xs text-slate-300 dark:border-slate-700 dark:text-slate-600">
            {mobile ? 'Nothing here yet.' : 'Drop here'}
          </p>
        )}

        {/* Older accepted Change Requests, collapsed so Done can't grow unbounded (mirrors "Closed"). */}
        {!!olderItems?.length && (
          <div className="mt-1 rounded-md border border-slate-200 bg-white/60 dark:border-slate-700 dark:bg-slate-900/40">
            <button
              onClick={() => setShowOlder((v) => !v)}
              className="flex w-full items-center gap-1.5 px-2 py-1.5 text-left text-[11px] font-medium text-slate-500 hover:text-slate-700 dark:hover:text-slate-300"
            >
              <span>{showOlder ? '▾' : '▸'}</span> {olderItems.length} older
            </button>
            {showOlder && (
              <ul className="space-y-0.5 px-2 pb-2">
                {olderItems.map((c) => (
                  <li key={c.id}>
                    <button
                      onClick={() => onSelect(c.id)}
                      className="w-full truncate text-left text-xs text-slate-500 hover:text-slate-800 dark:hover:text-slate-200"
                      title={c.title}
                    >
                      {c.displayKey && <span className="font-mono text-slate-400">{c.displayKey} </span>}
                      {c.title}
                      {c.completedAt && <span className="text-slate-400"> · accepted {relativeTime(c.completedAt)} ago</span>}
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
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

// Compact chip labels — the two camel-case stages would otherwise crowd the strip.
const STAGE_SHORT_LABEL: Partial<Record<ChangeStatus, string>> = {
  InDevelopment: 'In Dev',
  InReview: 'In Review',
}
const stageLabel = (status: ChangeStatus) => STAGE_SHORT_LABEL[status] ?? status

/**
 * Mobile-only stage picker. Chevrons step to the adjacent stage; the horizontally scrollable chip
 * strip jumps to any stage and shows each stage's count. The 🔒 gate marker sits between Triaged and
 * Approved, mirroring the desktop divider. The active chip scrolls into view when the stage changes
 * (e.g. via a swipe or chevron), so it stays visible in the strip.
 */
function MobileStageSwitcher({
  active, onSelect, countFor,
}: {
  active: ChangeStatus
  onSelect: (status: ChangeStatus) => void
  countFor: (status: ChangeStatus) => number
}) {
  const idx = LIFECYCLE_COLUMNS.indexOf(active)
  const activeRef = useRef<HTMLButtonElement>(null)
  useEffect(() => {
    activeRef.current?.scrollIntoView({ inline: 'center', block: 'nearest', behavior: 'smooth' })
  }, [active])
  const step = (delta: number) => {
    const next = idx + delta
    if (next >= 0 && next < LIFECYCLE_COLUMNS.length) onSelect(LIFECYCLE_COLUMNS[next])
  }
  return (
    <div className="flex items-center gap-1">
      <button
        onClick={() => step(-1)}
        disabled={idx <= 0}
        aria-label="Previous stage"
        className="shrink-0 rounded-md px-2 py-1.5 text-lg leading-none text-slate-500 hover:bg-slate-100 disabled:opacity-30 dark:text-slate-400 dark:hover:bg-slate-800"
      >
        ‹
      </button>
      <div className="flex flex-1 items-center gap-1 overflow-x-auto">
        {LIFECYCLE_COLUMNS.map((status) => {
          const isActive = status === active
          return (
            <div key={status} className="flex shrink-0 items-center">
              {status === 'Approved' && (
                <span
                  title="Whitelist gate — Maintainer approval required"
                  className="mr-1 select-none rounded bg-violet-100 px-1 py-0.5 text-[10px] font-semibold text-violet-700 dark:bg-violet-500/20 dark:text-violet-300"
                >
                  🔒
                </span>
              )}
              <button
                ref={isActive ? activeRef : undefined}
                onClick={() => onSelect(status)}
                aria-current={isActive ? 'true' : undefined}
                className={cx(
                  'flex items-center gap-1.5 rounded-full border px-3 py-1.5 text-sm font-medium transition-colors',
                  isActive
                    ? 'border-indigo-300 bg-indigo-50 text-indigo-700 dark:border-indigo-500/40 dark:bg-indigo-500/15 dark:text-indigo-200'
                    : 'border-transparent text-slate-500 hover:bg-slate-100 dark:text-slate-400 dark:hover:bg-slate-800',
                )}
              >
                <span>{stageLabel(status)}</span>
                <Badge tone={isActive ? STATUS_TONE[status] : 'slate'}>{countFor(status)}</Badge>
              </button>
            </div>
          )
        })}
      </div>
      <button
        onClick={() => step(1)}
        disabled={idx >= LIFECYCLE_COLUMNS.length - 1}
        aria-label="Next stage"
        className="shrink-0 rounded-md px-2 py-1.5 text-lg leading-none text-slate-500 hover:bg-slate-100 disabled:opacity-30 dark:text-slate-400 dark:hover:bg-slate-800"
      >
        ›
      </button>
    </div>
  )
}
