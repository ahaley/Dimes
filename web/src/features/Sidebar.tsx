import { useEffect, useState } from 'react'
import { DndContext, pointerWithin, type DragEndEvent } from '@dnd-kit/core'
import { SortableContext, arrayMove, useSortable, verticalListSortingStrategy } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import type { Project, ProjectQuota } from '../api/types'
import { useReorderProjects } from '../api/hooks'
import { Badge, cx } from '../components/ui'
import { useBoardSensors } from '../lib/dndSensors'
import { initials, projectColor } from '../lifecycle'
import { projectLimitMessage } from '../projectQuota'

export function Sidebar({
  projects, otherProjects = [], archivedProjects = [], assignmentCounts, siteTitle, projectId, onSelect, collapsed, onToggleCollapse,
  canCreateProject, projectQuota, onNewProject,
  activeView, onShowProviders, onShowActors, onShowSettings, showSettings, mobileOpen,
}: {
  projects: Project[]
  /** Projects the viewer can see but isn't a member of — only ever non-empty for a site admin. */
  otherProjects?: Project[]
  archivedProjects?: Project[]
  assignmentCounts: Map<string, number>
  siteTitle: string
  projectId: string | undefined
  onSelect: (id: string) => void
  collapsed: boolean
  onToggleCollapse: () => void
  canCreateProject: boolean
  /** The viewer's creation allowance. When it's spent, the rail explains why in the button's place
   *  rather than leaving an unexplained gap. Undefined while still loading. */
  projectQuota?: ProjectQuota
  onNewProject: () => void
  activeView: 'board' | 'providers' | 'actors' | 'settings'
  onShowProviders: () => void
  onShowActors: () => void
  onShowSettings: () => void
  showSettings: boolean
  mobileOpen: boolean
}) {
  // "collapsed" is a desktop-only rail concept; the mobile drawer always shows full labels.
  const compact = collapsed && !mobileOpen
  const [showArchived, setShowArchived] = useState(false)
  const [showOthers, setShowOthers] = useState(false)

  // Personal project ordering via drag (expanded mode only). The grip handle carries the drag; the row
  // itself stays click-to-navigate. A 5px move (mouse) or 250ms long-press (touch) starts the drag, so
  // a tap/quick click navigates and a swipe scrolls the list instead of grabbing a row.
  const reorder = useReorderProjects()
  const sensors = useBoardSensors()
  const onProjectDragEnd = (e: DragEndEvent) => {
    const overId = e.over?.id as string | undefined
    if (!overId || overId === e.active.id) return
    const from = projects.findIndex((p) => p.id === e.active.id)
    const to = projects.findIndex((p) => p.id === overId)
    if (from === -1 || to === -1) return
    reorder.mutate({ orderedIds: arrayMove(projects.map((p) => p.id), from, to) })
  }
  return (
    <aside
      className={cx(
        'flex h-screen w-60 shrink-0 flex-col border-r border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900',
        // Off-canvas overlay drawer below md; static rail at md+.
        'fixed inset-y-0 left-0 z-40 transform transition-transform duration-150 md:static md:z-auto md:translate-x-0',
        mobileOpen ? 'translate-x-0 shadow-xl md:shadow-none' : '-translate-x-full',
        collapsed ? 'md:w-14' : 'md:w-60',
      )}
    >
      {/* Brand + collapse toggle */}
      <div className={cx('flex items-center px-3 py-3', compact ? 'justify-center' : 'justify-between')}>
        {!compact && <span className="truncate text-lg font-semibold tracking-tight text-indigo-700">{siteTitle}</span>}
        <button
          onClick={onToggleCollapse}
          title={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
          aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
          className={cx(
            'hidden rounded-md p-1.5 text-slate-500 hover:bg-slate-100 hover:text-slate-700 md:block dark:hover:bg-slate-800 dark:hover:text-slate-200',
            compact && 'flex h-8 w-8 items-center justify-center font-semibold text-indigo-700',
          )}
        >
          {compact ? siteTitle.charAt(0).toUpperCase() : '«'}
        </button>
      </div>

      {!compact && (
        <div className="px-3 pb-1 text-xs font-semibold uppercase tracking-wide text-slate-400">Projects</div>
      )}

      {/* Project list */}
      <nav className="flex-1 space-y-1 overflow-y-auto px-2 py-1">
        {compact
          ? projects.map((p) => {
            const active = p.id === projectId && activeView === 'board'
            const assigned = assignmentCounts.get(p.id) ?? 0
            return (
              <button
                key={p.id}
                onClick={() => onSelect(p.id)}
                title={assigned > 0 ? `${p.name} — ${assigned} newly assigned to you` : p.name}
                className={cx(
                  'relative flex h-9 w-9 items-center justify-center rounded-md text-sm',
                  active
                    ? 'bg-slate-100 font-medium text-slate-900 dark:bg-slate-800 dark:text-slate-100'
                    : 'text-slate-600 hover:bg-slate-50 dark:text-slate-300 dark:hover:bg-slate-800',
                )}
              >
                <span
                  className={cx(
                    'flex h-7 w-7 items-center justify-center rounded-md text-xs font-semibold',
                    active ? 'bg-indigo-600 text-white' : projectColor(p.id),
                  )}
                >
                  {initials(p.name)}
                </span>
                {assigned > 0 && (
                  <span
                    aria-label={`${assigned} newly assigned to you`}
                    className="absolute right-0.5 top-0.5 h-2 w-2 rounded-full bg-indigo-500 ring-2 ring-white dark:ring-slate-900"
                  />
                )}
              </button>
            )
          })
          : (
            <DndContext sensors={sensors} collisionDetection={pointerWithin} onDragEnd={onProjectDragEnd}>
              <SortableContext items={projects.map((p) => p.id)} strategy={verticalListSortingStrategy}>
                {projects.map((p) => (
                  <SortableProjectRow
                    key={p.id}
                    project={p}
                    active={p.id === projectId && activeView === 'board'}
                    assignedCount={assignmentCounts.get(p.id) ?? 0}
                    onSelect={onSelect}
                  />
                ))}
              </SortableContext>
            </DndContext>
          )}

        {/* Creating a project spends a per-user allowance (site admins are unlimited). The API enforces
            it; once it's spent the button is dimmed rather than removed, and explains itself on demand —
            so the affordance never vanishes unexplained, but doesn't nag someone content at their limit. */}
        {canCreateProject ? (
          <button
            onClick={onNewProject}
            title="New project"
            className={cx(
              'flex w-full items-center rounded-md text-sm text-slate-500 hover:bg-slate-50 hover:text-slate-700 dark:text-slate-400 dark:hover:bg-slate-800 dark:hover:text-slate-200',
              compact ? 'h-9 w-9 justify-center' : 'gap-2 px-2 py-1.5',
            )}
          >
            <span className="text-base leading-none">+</span>
            {!compact && <span>New project</span>}
          </button>
        ) : projectQuota && !projectQuota.unlimited ? (
          <ProjectLimitNotice quota={projectQuota} compact={compact} />
        ) : null}

        {/* Archived projects: hidden from the active list, reachable here to view or unarchive. */}
        {!compact && archivedProjects.length > 0 && (
          <div className="pt-2">
            <button
              onClick={() => setShowArchived((v) => !v)}
              className="flex w-full items-center gap-1 rounded-md px-2 py-1.5 text-left text-xs font-semibold uppercase tracking-wide text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
            >
              <span className="text-[0.65rem]">{showArchived ? '▾' : '▸'}</span>
              <span>Archived ({archivedProjects.length})</span>
            </button>
            {showArchived && archivedProjects.map((p) => {
              const active = p.id === projectId && activeView === 'board'
              return (
                <button
                  key={p.id}
                  onClick={() => onSelect(p.id)}
                  title={p.name}
                  className={cx(
                    'flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm',
                    active
                      ? 'bg-slate-100 font-medium text-slate-700 dark:bg-slate-800 dark:text-slate-200'
                      : 'text-slate-400 hover:bg-slate-50 dark:text-slate-500 dark:hover:bg-slate-800',
                  )}
                >
                  <span className={cx('h-4 w-0.5 rounded', active ? 'bg-slate-400' : 'bg-transparent')} />
                  <span className="truncate italic">{p.name}</span>
                </button>
              )
            })}
          </div>
        )}

        {/* Projects the viewer isn't a member of: kept out of the menu proper (they're someone else's
            project until you join), but reachable here so a site admin can still get into one. Empty
            for everyone else, so this group simply doesn't exist for them. These are the projects
            whose creator an admin most needs to identify, so the tooltip names them. */}
        {!compact && otherProjects.length > 0 && (
          <div className="pt-2">
            <button
              onClick={() => setShowOthers((v) => !v)}
              className="flex w-full items-center gap-1 rounded-md px-2 py-1.5 text-left text-xs font-semibold uppercase tracking-wide text-slate-400 hover:text-slate-600 dark:hover:text-slate-200"
            >
              <span className="text-[0.65rem]">{showOthers ? '▾' : '▸'}</span>
              <span>All projects ({otherProjects.length})</span>
            </button>
            {showOthers && otherProjects.map((p) => {
              const active = p.id === projectId && activeView === 'board'
              return (
                <button
                  key={p.id}
                  onClick={() => onSelect(p.id)}
                  title={
                    `${p.name} — you're not a member`
                    + (p.createdByDisplayName ? ` · created by ${p.createdByDisplayName}` : '')
                  }
                  className={cx(
                    'flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm',
                    active
                      ? 'bg-slate-100 font-medium text-slate-700 dark:bg-slate-800 dark:text-slate-200'
                      : 'text-slate-400 hover:bg-slate-50 dark:text-slate-500 dark:hover:bg-slate-800',
                  )}
                >
                  <span className={cx('h-4 w-0.5 rounded', active ? 'bg-slate-400' : 'bg-transparent')} />
                  <span className="truncate">{p.name}</span>
                </button>
              )
            })}
          </div>
        )}

        {/* App-level settings — LLM providers + Actors are site-admin-only (gated by showSettings). */}
        {showSettings && (
        <div className="pt-3">
          {!compact && (
            <div className="px-2 pb-1 text-xs font-semibold uppercase tracking-wide text-slate-400">Settings</div>
          )}
          <button
            onClick={onShowProviders}
            title="LLM providers"
            className={cx(
              'flex w-full items-center rounded-md text-sm',
              compact ? 'h-9 w-9 justify-center' : 'gap-2 px-2 py-1.5 text-left',
              activeView === 'providers'
                ? 'bg-slate-100 font-medium text-slate-900 dark:bg-slate-800 dark:text-slate-100'
                : 'text-slate-600 hover:bg-slate-50 dark:text-slate-300 dark:hover:bg-slate-800',
            )}
          >
            <span aria-hidden>⚡</span>
            {!compact && <span>LLM providers</span>}
          </button>
          <button
            onClick={onShowActors}
            title="Actors"
            className={cx(
              'flex w-full items-center rounded-md text-sm',
              compact ? 'h-9 w-9 justify-center' : 'gap-2 px-2 py-1.5 text-left',
              activeView === 'actors'
                ? 'bg-slate-100 font-medium text-slate-900 dark:bg-slate-800 dark:text-slate-100'
                : 'text-slate-600 hover:bg-slate-50 dark:text-slate-300 dark:hover:bg-slate-800',
            )}
          >
            <span aria-hidden>👤</span>
            {!compact && <span>Actors</span>}
          </button>
        </div>
        )}
      </nav>

      {showSettings && (
        <button
          onClick={onShowSettings}
          title="Site settings"
          className={cx(
            'flex w-full items-center border-t border-slate-200 text-sm dark:border-slate-800',
            compact ? 'h-12 justify-center' : 'gap-2 px-4 py-3 text-left',
            activeView === 'settings'
              ? 'bg-slate-100 font-medium text-slate-900 dark:bg-slate-800 dark:text-slate-100'
              : 'text-slate-600 hover:bg-slate-50 dark:text-slate-300 dark:hover:bg-slate-800',
          )}
        >
          <span aria-hidden>⚙</span>
          {!compact && <span>Site settings</span>}
        </button>
      )}
    </aside>
  )
}

/// Stands in for the "New project" button once the viewer can't create one, so the affordance doesn't
/// just disappear. It stays a dimmed, smaller "+ New project" — someone settled at their limit sees only
/// that, not a standing explanation they've already read — and reveals the reason on hover or click. Click
/// pins it open so touch users (who have no hover) and anyone wanting to read slowly can keep it up.
/// The wording lives in `projectLimitMessage` so this and the no-projects empty state can't drift apart.
/// Collapsed to a rail there's no room for a panel, so the native tooltip carries the same text.
function ProjectLimitNotice({ quota, compact }: { quota: ProjectQuota; compact: boolean }) {
  const { heading, detail } = projectLimitMessage(quota)
  const [pinned, setPinned] = useState(false)
  const [hovered, setHovered] = useState(false)
  const open = pinned || hovered

  // Esc closes a pinned panel, matching OverflowMenu.
  useEffect(() => {
    if (!pinned) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setPinned(false)
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [pinned])

  if (compact) {
    return (
      <div
        title={`${heading} — ${detail}`}
        className="flex h-9 w-9 items-center justify-center rounded-md text-slate-300 dark:text-slate-600"
      >
        <span aria-hidden>+</span>
        <span className="sr-only">{`New project unavailable. ${heading}`}</span>
      </div>
    )
  }

  return (
    // Hover handlers sit on the wrapper, not the button: the panel is a descendant, so moving the
    // pointer into it doesn't count as leaving and the panel doesn't flicker shut on the way down.
    <div
      className="relative"
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
    >
      <button
        type="button"
        // Not disabled, and deliberately not aria-disabled either: this control is genuinely interactive
        // — it discloses why creation is unavailable — so marking it unavailable would tell a screen
        // reader not to use the one thing that explains the situation. The unavailability lives in the
        // accessible name instead, which still contains the visible "New project" text.
        aria-label={`New project unavailable — ${heading}`}
        aria-expanded={open}
        onClick={() => setPinned((v) => !v)}
        onFocus={() => setHovered(true)}
        onBlur={() => setHovered(false)}
        className="flex w-full cursor-help items-center gap-2 rounded-md px-2 py-1 text-xs text-slate-300 hover:text-slate-400 dark:text-slate-600 dark:hover:text-slate-500"
      >
        <span className="text-sm leading-none">+</span>
        <span>New project</span>
      </button>
      {open && (
        <>
          {pinned && <div className="fixed inset-0 z-10" onClick={() => setPinned(false)} />}
          {/* The outer box carries the offset as padding rather than a margin, so the gap between
              button and panel is still inside the hover region. */}
          <div className="absolute left-0 right-0 top-full z-20 pt-1">
            <div
              role="note"
              className="rounded-md border border-slate-200 bg-white p-2.5 shadow-lg dark:border-slate-700 dark:bg-slate-800"
            >
              <p className="text-xs font-medium text-slate-600 dark:text-slate-300">{heading}</p>
              <p className="mt-1 text-xs leading-relaxed text-slate-500 dark:text-slate-400">{detail}</p>
            </div>
          </div>
        </>
      )}
    </div>
  )
}

/// An active-project row in the expanded sidebar: drag the hover-revealed grip (∷) to reorder; click
/// anywhere else to open the project. Drag listeners live only on the handle so navigation is untouched.
function SortableProjectRow({
  project: p, active, assignedCount, onSelect,
}: { project: Project; active: boolean; assignedCount: number; onSelect: (id: string) => void }) {
  const { setNodeRef, transform, transition, attributes, listeners, isDragging } = useSortable({ id: p.id })
  const style = { transform: CSS.Transform.toString(transform), transition }
  return (
    <div
      ref={setNodeRef}
      style={style}
      className={cx(
        'group/proj flex items-center rounded-md text-sm',
        isDragging && 'opacity-50',
        active
          ? 'bg-slate-100 font-medium text-slate-900 dark:bg-slate-800 dark:text-slate-100'
          : 'text-slate-600 hover:bg-slate-50 dark:text-slate-300 dark:hover:bg-slate-800',
      )}
    >
      <button
        {...attributes}
        {...listeners}
        aria-label={`Reorder ${p.name}`}
        title="Drag to reorder"
        className="cursor-grab touch-none px-1 py-1.5 text-slate-300 opacity-100 transition-opacity hover:text-slate-500 focus:opacity-100 active:cursor-grabbing md:opacity-0 md:group-hover/proj:opacity-100 dark:text-slate-600 dark:hover:text-slate-400"
      >
        ∷
      </button>
      <button onClick={() => onSelect(p.id)} title={p.name} className="flex min-w-0 flex-1 items-center gap-2 py-1.5 pr-2 text-left">
        {/* Active accent bar, then a stable colored monogram for fast at-a-glance project recognition. */}
        <span className={cx('h-7 w-0.5 shrink-0 rounded', active ? 'bg-indigo-600' : 'bg-transparent')} />
        <span className={cx('flex h-7 w-7 shrink-0 items-center justify-center rounded-md text-[11px] font-semibold', projectColor(p.id))}>
          {initials(p.name)}
        </span>
        <span className="flex min-w-0 flex-col">
          <span className={cx('truncate leading-tight', active ? 'font-semibold' : 'font-medium')}>{p.name}</span>
          {p.key && <span className="truncate font-mono text-[11px] uppercase tracking-wide leading-tight text-slate-400 dark:text-slate-300">{p.key}</span>}
        </span>
        {assignedCount > 0 && (
          <span className="ml-auto shrink-0" title={`${assignedCount} newly assigned to you`}>
            <Badge tone="indigo">{assignedCount}</Badge>
          </span>
        )}
      </button>
    </div>
  )
}
