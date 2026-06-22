import { useState } from 'react'
import type { Project } from '../api/types'
import { cx } from '../components/ui'
import { initials } from '../lifecycle'

export function Sidebar({
  projects, archivedProjects = [], projectId, onSelect, collapsed, onToggleCollapse, onNewProject,
  activeView, onShowProviders, onShowActors, onShowSettings, showSettings, mobileOpen,
}: {
  projects: Project[]
  archivedProjects?: Project[]
  projectId: string | undefined
  onSelect: (id: string) => void
  collapsed: boolean
  onToggleCollapse: () => void
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
        {!compact && <span className="text-lg font-semibold tracking-tight text-indigo-700">Dimes</span>}
        <button
          onClick={onToggleCollapse}
          title={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
          aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
          className={cx(
            'hidden rounded-md p-1.5 text-slate-500 hover:bg-slate-100 hover:text-slate-700 md:block dark:hover:bg-slate-800 dark:hover:text-slate-200',
            compact && 'flex h-8 w-8 items-center justify-center font-semibold text-indigo-700',
          )}
        >
          {compact ? 'D' : '«'}
        </button>
      </div>

      {!compact && (
        <div className="px-3 pb-1 text-xs font-semibold uppercase tracking-wide text-slate-400">Projects</div>
      )}

      {/* Project list */}
      <nav className="flex-1 space-y-1 overflow-y-auto px-2 py-1">
        {projects.map((p) => {
          const active = p.id === projectId && activeView === 'board'
          return (
            <button
              key={p.id}
              onClick={() => onSelect(p.id)}
              title={p.name}
              className={cx(
                'flex w-full items-center rounded-md text-sm',
                compact ? 'h-9 w-9 justify-center' : 'gap-2 px-2 py-1.5 text-left',
                active
                  ? 'bg-slate-100 font-medium text-slate-900 dark:bg-slate-800 dark:text-slate-100'
                  : 'text-slate-600 hover:bg-slate-50 dark:text-slate-300 dark:hover:bg-slate-800',
              )}
            >
              {compact ? (
                <span
                  className={cx(
                    'flex h-7 w-7 items-center justify-center rounded-md text-xs font-semibold',
                    active ? 'bg-indigo-600 text-white' : 'bg-slate-200 text-slate-600 dark:bg-slate-700 dark:text-slate-300',
                  )}
                >
                  {initials(p.name)}
                </span>
              ) : (
                <>
                  <span className={cx('h-4 w-0.5 rounded', active ? 'bg-indigo-600' : 'bg-transparent')} />
                  <span className="truncate">{p.name}</span>
                </>
              )}
            </button>
          )
        })}

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

        {/* App-level settings */}
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
