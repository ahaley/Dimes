import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { DndContext, PointerSensor, pointerWithin, rectIntersection, useDroppable, useSensor, useSensors, type CollisionDetection, type DragEndEvent, type DragOverEvent } from '@dnd-kit/core'
import { SortableContext, arrayMove, verticalListSortingStrategy } from '@dnd-kit/sortable'
import { useActors, useAddEpicChild, useChanges, useRemoveEpicChild, useReorderChanges, useTransition } from '../api/hooks'
import { LIFECYCLE_COLUMNS, type ChangeRequest, type ChangeStatus, type Member } from '../api/types'
import { STATUS_TONE, relativeTime } from '../lifecycle'
import { Badge, cx } from '../components/ui'
import { useToast } from '../components/Toast'
import { ChangeCard } from './ChangeCard'

// The Done column keeps recently-accepted Change Requests visible and collapses older ones so it can't
// grow without bound. "Recent" = accepted within this window (by CompletedAt); a Done change with no
// CompletedAt (e.g. pre-feature, before the startup backfill stamps it) counts as older.
const DONE_RECENT_DAYS = 14
function isRecentlyAccepted(c: ChangeRequest): boolean {
  return c.completedAt != null && Date.now() - new Date(c.completedAt).getTime() < DONE_RECENT_DAYS * 864e5
}

// Dragging a request onto an Epic in the same column adds it to the Epic's composition — but only after
// the cursor dwells on the Epic this long, so a quick pass-over still reads as an ordinary reorder.
const COMPOSE_DWELL_MS = 600

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
  const addChild = useAddEpicChild(projectId)
  const removeChild = useRemoveEpicChild(projectId)
  const toast = useToast()
  const navigate = useNavigate()
  const [closedOpen, setClosedOpen] = useState(false)
  // Drag-to-compose: the Epic currently armed as a composition drop target (cursor has dwelled on it).
  // `armedRef` mirrors the state so onDragEnd can read it synchronously; `overRef` tracks the current
  // hovered droppable so the dwell timer only restarts when the target actually changes; `timerRef`
  // holds the pending arm timeout.
  const [armedEpicId, setArmedEpicId] = useState<string | null>(null)
  const armedRef = useRef<string | null>(null)
  const overRef = useRef<string | undefined>(undefined)
  const timerRef = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)
  const setArmed = (id: string | null) => { armedRef.current = id; setArmedEpicId(id) }
  const clearArm = () => {
    if (timerRef.current) clearTimeout(timerRef.current)
    timerRef.current = undefined
    overRef.current = undefined
    setArmed(null)
  }
  useEffect(() => () => { if (timerRef.current) clearTimeout(timerRef.current) }, [])
  // Which Epic cards are expanded to reveal their composed children (board-local UI state).
  const [expanded, setExpanded] = useState<Set<string>>(new Set())
  const toggleExpand = (id: string) =>
    setExpanded((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

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

  // Apply the board filters: the text query AND, when the "assigned to me" toggle is on, the current actor.
  const allChanges = changes ?? []
  const matches = (c: ChangeRequest) =>
    matchesQuery(c, query) && (!mineOnly || c.assigneeActorId === actingActorId)

  // Composed children render nested inside their Epic card, never as their own column cards. Group every
  // child under its parent so the Epic card can show them.
  const childrenByEpic = new Map<string, ChangeRequest[]>()
  for (const c of allChanges) {
    if (c.parentChangeRequestId) {
      const arr = childrenByEpic.get(c.parentChangeRequestId) ?? []
      arr.push(c)
      childrenByEpic.set(c.parentChangeRequestId, arr)
    }
  }
  // The children of an Epic that pass the current board filters, ordered the way a column would order them.
  const shownChildren = (epicId: string) =>
    (childrenByEpic.get(epicId) ?? [])
      .filter(matches)
      .sort((a, b) => a.sortOrder - b.sortOrder || b.updatedAt.localeCompare(a.updatedAt))

  // Top-level cards = everything that isn't itself a composed child. An Epic stays visible if it matches
  // OR any of its children match, so a child hit isn't hidden behind a non-matching Epic.
  const isVisible = (c: ChangeRequest) =>
    c.kind === 'Epic' ? matches(c) || shownChildren(c.id).length > 0 : matches(c)
  const visibleTop = allChanges.filter((c) => !c.parentChangeRequestId && isVisible(c))

  // Mirror the server order (OrderBy SortOrder, then UpdatedAt desc) so an optimistic SortOrder change
  // from a drag reorders the column immediately — otherwise the card snaps back until the refetch lands.
  const byStatus = (status: ChangeStatus) =>
    visibleTop
      .filter((c) => c.status === status)
      .sort((a, b) => a.sortOrder - b.sortOrder || b.updatedAt.localeCompare(a.updatedAt))
  const terminal = visibleTop.filter((c) => c.status === 'Rejected' || c.status === 'Duplicate')

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

  // Break a composed child out of its Epic (from the board's long-press affordance). The child keeps its
  // status and reappears as a standalone card in its column after the invalidation refresh.
  const requestRemoveChild = (child: ChangeRequest) => {
    if (!child.parentChangeRequestId) return
    removeChild.mutate(
      { epicId: child.parentChangeRequestId, childId: child.id },
      {
        onSuccess: () => toast.success(`Removed “${child.title}” from the Epic`),
        onError: (e) => toast.error(e instanceof Error ? e.message : 'Could not remove from Epic'),
      },
    )
  }

  const isStatusId = (id: string): id is ChangeStatus =>
    (LIFECYCLE_COLUMNS as string[]).includes(id)

  // A request can be composed into an Epic by drag only when it's a same-column, non-Epic, not-already-
  // composed card and the target is a (different) Epic. Mirrors the server's add-child guards.
  const canCompose = (active?: ChangeRequest, over?: ChangeRequest) =>
    !!active && !!over && over.id !== active.id &&
    over.kind === 'Epic' && active.kind !== 'Epic' &&
    active.parentChangeRequestId == null && over.status === active.status

  // Arm the hovered Epic for composition once the cursor has dwelled on it. Only (re)start the timer when
  // the hovered target changes, so holding still over an Epic lets the dwell complete.
  const onDragOver = (e: DragOverEvent) => {
    const overId = e.over?.id as string | undefined
    if (overRef.current === overId) return
    overRef.current = overId
    if (timerRef.current) clearTimeout(timerRef.current)
    setArmed(null)
    const active = (changes ?? []).find((c) => c.id === e.active.id)
    const over = overId ? (changes ?? []).find((c) => c.id === overId) : undefined
    if (canCompose(active, over)) {
      timerRef.current = setTimeout(() => setArmed(over!.id), COMPOSE_DWELL_MS)
    }
  }

  const onDragCancel = () => clearArm()

  const onDragEnd = (e: DragEndEvent) => {
    const armed = armedRef.current
    clearArm()

    const overId = e.over?.id as string | undefined
    if (!overId) return
    const change = (changes ?? []).find((c) => c.id === e.active.id)
    if (!change) return

    // Drag-to-compose: released while an Epic was armed (the cursor dwelled on it) → add to its composition
    // instead of reordering past it.
    if (armed && overId === armed) {
      const epic = (changes ?? []).find((c) => c.id === armed)
      if (epic && canCompose(change, epic)) {
        addChild.mutate(
          { epicId: epic.id, childId: change.id },
          {
            onSuccess: () => toast.success(`Added “${change.title}” to ${epic.displayKey ?? 'the Epic'}`),
            onError: (err) => toast.error(err instanceof Error ? err.message : 'Could not add to Epic'),
          },
        )
        return
      }
    }

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

  return (
    <DndContext sensors={sensors} collisionDetection={boardCollisionDetection} onDragOver={onDragOver} onDragEnd={onDragEnd} onDragCancel={onDragCancel}>
      <div className="flex gap-3 overflow-x-auto pb-3">
        {LIFECYCLE_COLUMNS.map((status) => {
          // Done is split into recently-accepted (shown) and older (collapsed) Change Requests.
          const inColumn = byStatus(status)
          const recent = status === 'Done' ? inColumn.filter(isRecentlyAccepted) : inColumn
          const older = status === 'Done' ? inColumn.filter((c) => !isRecentlyAccepted(c)) : undefined
          return (
            <div key={status} className="flex">
              {status === 'Approved' && <Gate />}
              <Column
                status={status}
                changes={recent}
                olderItems={older}
                members={members}
                authorOf={authorOf}
                onSelect={onSelect}
                onTransition={requestTransition}
                childrenOf={shownChildren}
                expandedIds={expanded}
                onToggleExpand={toggleExpand}
                onRemoveChild={requestRemoveChild}
                armedEpicId={armedEpicId}
                onFocus={() => navigate(`/projects/${projectId}/focus/${status}`)}
              />
            </div>
          )
        })}
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
  status, changes, olderItems, members, authorOf, onSelect, onTransition, childrenOf, expandedIds, onToggleExpand, onRemoveChild, armedEpicId, onFocus,
}: {
  status: ChangeStatus
  changes: ChangeRequest[]
  olderItems?: ChangeRequest[]
  members: Member[]
  authorOf: (change: ChangeRequest) => string
  onSelect: (id: string) => void
  onTransition: (change: ChangeRequest, target: ChangeStatus) => void
  childrenOf: (epicId: string) => ChangeRequest[]
  expandedIds: Set<string>
  onToggleExpand: (id: string) => void
  onRemoveChild: (child: ChangeRequest) => void
  armedEpicId: string | null
  onFocus: () => void
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
        'group/col flex w-64 shrink-0 flex-col rounded-lg border bg-slate-50/70 p-2 transition-colors dark:bg-slate-800/40',
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
            className="ml-auto rounded p-0.5 text-slate-400 opacity-0 transition-opacity hover:bg-slate-200 hover:text-slate-600 focus:opacity-100 group-hover/col:opacity-100 dark:hover:bg-slate-700 dark:hover:text-slate-200"
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
              epicChildren={c.kind === 'Epic' ? childrenOf(c.id) : undefined}
              expanded={expandedIds.has(c.id)}
              onToggleExpand={() => onToggleExpand(c.id)}
              onSelectChild={onSelect}
              onTransitionChild={onTransition}
              onRemoveChild={onRemoveChild}
              isDropTarget={c.id === armedEpicId}
            />
          ))}
        </SortableContext>
        {changes.length === 0 && !olderItems?.length && (
          <p className="rounded-md border border-dashed border-slate-200 px-2 py-6 text-center text-xs text-slate-300 dark:border-slate-700 dark:text-slate-600">
            Drop here
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
