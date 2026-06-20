import { useEffect, useState } from 'react'
import { api } from './api/client'
import { useMe, useMembers, useProjects } from './api/hooks'
import { Button, Card } from './components/ui'
import { Sidebar } from './features/Sidebar'
import { Workspace } from './features/Workspace'
import { LlmProvidersView } from './features/LlmProvidersView'
import { ActorsView } from './features/ActorsView'
import { SettingsModal } from './features/SettingsModal'
import { CreateProjectModal } from './features/CreateProjectModal'
import { LoginView } from './features/LoginView'
import { SiteSettingsView } from './features/SiteSettingsView'
import { applyTheme, getInitialTheme, type Theme } from './theme'

const COLLAPSE_KEY = 'dimes.sidebar.collapsed'

type View = 'board' | 'providers' | 'actors' | 'settings'

export default function App() {
  const { data: me, isLoading: meLoading, isError: loggedOut } = useMe()
  // Only fetch app data once we have a session — avoids 401 noise (and stuck-errored queries) on the login screen.
  const { data: projects } = useProjects(!!me)
  const [projectId, setProjectId] = useState<string>()
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
  const currentProject = (projects ?? []).find((p) => p.id === projectId)

  // Auth gate: wait for the session, then either show the login screen or the app.
  if (meLoading) {
    return <div className="flex h-screen items-center justify-center text-sm text-slate-400">Loading…</div>
  }
  if (loggedOut || !me) {
    return <LoginView />
  }

  const logout = async () => {
    await api.logout()
    window.location.reload()
  }

  const headerTitle =
    view === 'providers' ? 'LLM providers'
      : view === 'actors' ? 'Actors'
        : view === 'settings' ? 'Site settings'
          : (currentProject?.name ?? 'Dimes')

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
        onShowSettings={() => setView('settings')}
        showSettings={me.isSiteAdmin}
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
          <span className="font-semibold text-slate-800 dark:text-slate-100">{headerTitle}</span>

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

          <div className="ml-auto flex items-center gap-3">
            <span className="text-sm text-slate-600 dark:text-slate-300" title={me.email ?? undefined}>
              {me.displayName}
            </span>
            <Button variant="subtle" onClick={logout}>Sign out</Button>
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
          ) : view === 'settings' ? (
            <SiteSettingsView />
          ) : projectId ? (
            <Workspace projectId={projectId} actingActorId={me.actorId} members={members ?? []} />
          ) : (
            <Card className="p-10 text-center text-slate-500 dark:text-slate-400">
              {(projects ?? []).length === 0
                ? 'Create a project to get started — use “New project” in the sidebar.'
                : 'Select a project to start working.'}
            </Card>
          )}
        </main>
      </div>

      {showCreateProject && (
        <CreateProjectModal
          onClose={() => setShowCreateProject(false)}
          onCreated={(p) => {
            setProjectId(p.id)
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
