import type { Project } from '../api/types'
import { cx } from '../components/ui'
import { initials } from '../lifecycle'

export function Sidebar({
  projects, projectId, onSelect, collapsed, onToggleCollapse, onNewProject,
  activeView, onShowProviders, onShowActors,
}: {
  projects: Project[]
  projectId: string | undefined
  onSelect: (id: string) => void
  collapsed: boolean
  onToggleCollapse: () => void
  onNewProject: () => void
  activeView: 'board' | 'providers' | 'actors'
  onShowProviders: () => void
  onShowActors: () => void
}) {
  return (
    <aside
      className={cx(
        'flex h-screen shrink-0 flex-col border-r border-slate-200 bg-white transition-[width] duration-150',
        collapsed ? 'w-14' : 'w-60',
      )}
    >
      {/* Brand + collapse toggle */}
      <div className={cx('flex items-center px-3 py-3', collapsed ? 'justify-center' : 'justify-between')}>
        {!collapsed && <span className="text-lg font-semibold tracking-tight text-indigo-700">Dimes</span>}
        <button
          onClick={onToggleCollapse}
          title={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
          aria-label={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
          className={cx(
            'rounded-md p-1.5 text-slate-500 hover:bg-slate-100 hover:text-slate-700',
            collapsed && 'flex h-8 w-8 items-center justify-center font-semibold text-indigo-700',
          )}
        >
          {collapsed ? 'D' : '«'}
        </button>
      </div>

      {!collapsed && (
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
                collapsed ? 'h-9 w-9 justify-center' : 'gap-2 px-2 py-1.5 text-left',
                active
                  ? 'bg-slate-100 font-medium text-slate-900'
                  : 'text-slate-600 hover:bg-slate-50',
              )}
            >
              {collapsed ? (
                <span
                  className={cx(
                    'flex h-7 w-7 items-center justify-center rounded-md text-xs font-semibold',
                    active ? 'bg-indigo-600 text-white' : 'bg-slate-200 text-slate-600',
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
            'flex w-full items-center rounded-md text-sm text-slate-500 hover:bg-slate-50 hover:text-slate-700',
            collapsed ? 'h-9 w-9 justify-center' : 'gap-2 px-2 py-1.5',
          )}
        >
          <span className="text-base leading-none">+</span>
          {!collapsed && <span>New project</span>}
        </button>

        {/* App-level settings */}
        <div className="pt-3">
          {!collapsed && (
            <div className="px-2 pb-1 text-xs font-semibold uppercase tracking-wide text-slate-400">Settings</div>
          )}
          <button
            onClick={onShowProviders}
            title="LLM providers"
            className={cx(
              'flex w-full items-center rounded-md text-sm',
              collapsed ? 'h-9 w-9 justify-center' : 'gap-2 px-2 py-1.5 text-left',
              activeView === 'providers'
                ? 'bg-slate-100 font-medium text-slate-900'
                : 'text-slate-600 hover:bg-slate-50',
            )}
          >
            <span aria-hidden>⚡</span>
            {!collapsed && <span>LLM providers</span>}
          </button>
          <button
            onClick={onShowActors}
            title="Actors"
            className={cx(
              'flex w-full items-center rounded-md text-sm',
              collapsed ? 'h-9 w-9 justify-center' : 'gap-2 px-2 py-1.5 text-left',
              activeView === 'actors'
                ? 'bg-slate-100 font-medium text-slate-900'
                : 'text-slate-600 hover:bg-slate-50',
            )}
          >
            <span aria-hidden>👤</span>
            {!collapsed && <span>Actors</span>}
          </button>
        </div>
      </nav>
    </aside>
  )
}
