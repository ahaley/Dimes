import { useEffect, useState } from 'react'
import { useMembers, useProjects } from './api/hooks'
import { Card, Select } from './components/ui'
import { Sidebar } from './features/Sidebar'
import { Workspace } from './features/Workspace'
import { LlmProvidersView } from './features/LlmProvidersView'
import { ActorsView } from './features/ActorsView'
import { SettingsModal } from './features/SettingsModal'
import { CreateProjectModal } from './features/CreateProjectModal'
import { applyTheme, getInitialTheme, type Theme } from './theme'

const COLLAPSE_KEY = 'dimes.sidebar.collapsed'

type View = 'board' | 'providers' | 'actors'

export default function App() {
  const { data: projects } = useProjects()
  const [projectId, setProjectId] = useState<string>()
  const [actingActorId, setActingActorId] = useState<string>()
  const [view, setView] = useState<View>('board')
  const [showSettings, setShowSettings] = useState(false)
  const [showCreateProject, setShowCreateProject] = useState(false)
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem(COLLAPSE_KEY) === '1')
  const [theme, setTheme] = useState<Theme>(getInitialTheme)

  useEffect(() => { applyTheme(theme) }, [theme])
  const toggleTheme = () => setTheme((t) => (t === 'dark' ? 'light' : 'dark'))

  const toggleCollapsed = () => {
    setCollapsed((c) => {
      const next = !c
      localStorage.setItem(COLLAPSE_KEY, next ? '1' : '0')
      return next
    })
  }

  useEffect(() => {
    if (!projectId && projects && projects.length > 0) setProjectId(projects[0].id)
  }, [projects, projectId])

  const { data: members } = useMembers(projectId)
  useEffect(() => {
    if (members && members.length > 0 && !members.some((m) => m.actorId === actingActorId)) {
      setActingActorId(members[0].actorId)
    }
  }, [members, actingActorId])

  const currentProject = (projects ?? []).find((p) => p.id === projectId)

  return (
    <div className="flex h-screen">
      <Sidebar
        projects={projects ?? []}
        projectId={projectId}
        onSelect={(id) => { setProjectId(id); setView('board') }}
        collapsed={collapsed}
        onToggleCollapse={toggleCollapsed}
        onNewProject={() => setShowCreateProject(true)}
        activeView={view}
        onShowProviders={() => setView('providers')}
        onShowActors={() => setView('actors')}
      />

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-14 shrink-0 items-center gap-3 border-b border-slate-200 bg-white px-4 dark:border-slate-800 dark:bg-slate-900">
          <button
            onClick={toggleCollapsed}
            title={collapsed ? 'Expand sidebar' : 'Collapse sidebar'}
            aria-label="Toggle sidebar"
            className="rounded-md p-1.5 text-slate-500 hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-slate-800 dark:hover:text-slate-200"
          >
            ☰
          </button>
          <span className="font-semibold text-slate-800 dark:text-slate-100">
            {view === 'providers' ? 'LLM providers' : view === 'actors' ? 'Actors' : (currentProject?.name ?? 'Dimes')}
          </span>

          {view === 'board' && projectId && (
            <button
              onClick={() => setShowSettings(true)}
              title="Manage project"
              className="flex items-center gap-1.5 rounded-md px-2 py-1 text-sm text-slate-600 hover:bg-slate-100 hover:text-slate-700 dark:text-slate-300 dark:hover:bg-slate-800 dark:hover:text-slate-200"
            >
              <span aria-hidden>⚙</span>
              <span>Manage</span>
            </button>
          )}

          <div className="ml-auto flex items-center gap-2">
            {view === 'board' && (
              <>
                <span className="text-xs text-slate-500 dark:text-slate-400">Acting as</span>
                <Select
                  value={actingActorId ?? ''}
                  onChange={(e) => setActingActorId(e.target.value || undefined)}
                  className="min-w-40"
                  disabled={!members || members.length === 0}
                >
                  {(members ?? []).length === 0 && <option value="">No members</option>}
                  {(members ?? []).map((m) => (
                    <option key={m.actorId} value={m.actorId}>{m.displayName} · {m.role}</option>
                  ))}
                </Select>
              </>
            )}
            <button
              onClick={toggleTheme}
              title={theme === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}
              aria-label="Toggle dark mode"
              className="rounded-md p-1.5 text-slate-500 hover:bg-slate-100 hover:text-slate-700 dark:hover:bg-slate-800 dark:hover:text-slate-200"
            >
              {theme === 'dark' ? '☀' : '☾'}
            </button>
          </div>
        </header>

        <main className="min-h-0 flex-1 overflow-auto p-6">
          {view === 'providers' ? (
            <LlmProvidersView projectId={projectId} />
          ) : view === 'actors' ? (
            <ActorsView />
          ) : projectId && actingActorId ? (
            <Workspace projectId={projectId} actingActorId={actingActorId} members={members ?? []} />
          ) : (
            <Card className="p-10 text-center text-slate-500 dark:text-slate-400">
              {(projects ?? []).length === 0
                ? 'Create a project to get started — use “New project” in the sidebar.'
                : 'Add a member to this project (Manage) to start working.'}
            </Card>
          )}
        </main>
      </div>

      {showCreateProject && (
        <CreateProjectModal
          onClose={() => setShowCreateProject(false)}
          onCreated={(p) => {
            setProjectId(p.id)
            setActingActorId(undefined)
            setShowCreateProject(false)
            setShowSettings(true)
          }}
        />
      )}
      {showSettings && projectId && (
        <SettingsModal projectId={projectId} onClose={() => setShowSettings(false)} />
      )}
    </div>
  )
}
