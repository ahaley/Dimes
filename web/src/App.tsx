import { useEffect, useState } from 'react'
import { Navigate, Route, Routes, useLocation, useMatch, useNavigate } from 'react-router-dom'
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
  const { data: projects } = useProjects(!!me)
  const navigate = useNavigate()
  const location = useLocation()

  // Current project comes from the route (/projects/:projectId[/changes/:changeId]).
  const projectMatch = useMatch('/projects/:projectId/*')
  const projectId = projectMatch?.params.projectId

  const [lastProjectId, setLastProjectId] = useState<string>()
  useEffect(() => { if (projectId) setLastProjectId(projectId) }, [projectId])

  const [showSettings, setShowSettings] = useState(false)
  const [showCreateProject, setShowCreateProject] = useState(false)
  const [collapsed, setCollapsed] = useState(() => localStorage.getItem(COLLAPSE_KEY) === '1')
  const [mobileOpen, setMobileOpen] = useState(false)
  const [theme, setTheme] = useState<Theme>(getInitialTheme)

  useEffect(() => { applyTheme(theme) }, [theme])
  // Close the mobile drawer whenever the route changes (covers sidebar nav taps).
  useEffect(() => { setMobileOpen(false) }, [location.pathname])
  const toggleTheme = () => setTheme((t) => (t === 'dark' ? 'light' : 'dark'))

  const toggleCollapsed = () => {
    setCollapsed((c) => {
      const next = !c
      localStorage.setItem(COLLAPSE_KEY, next ? '1' : '0')
      return next
    })
  }

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

  const view: View =
    location.pathname.startsWith('/providers') ? 'providers'
      : location.pathname.startsWith('/actors') ? 'actors'
        : location.pathname.startsWith('/settings') ? 'settings'
          : 'board'

  const headerTitle =
    view === 'providers' ? 'LLM providers'
      : view === 'actors' ? 'Actors'
        : view === 'settings' ? 'Site settings'
          : (currentProject?.name ?? 'Dimes')

  return (
    <div className="flex h-screen">
      {mobileOpen && (
        <div
          className="fixed inset-0 z-30 bg-slate-900/40 md:hidden"
          aria-hidden
          onClick={() => setMobileOpen(false)}
        />
      )}
      <Sidebar
        projects={projects ?? []}
        projectId={projectId}
        onSelect={(id) => navigate(`/projects/${id}`)}
        collapsed={collapsed}
        onToggleCollapse={toggleCollapsed}
        onNewProject={() => setShowCreateProject(true)}
        activeView={view}
        onShowProviders={() => navigate('/providers')}
        onShowActors={() => navigate('/actors')}
        onShowSettings={() => navigate('/settings')}
        showSettings={me.isSiteAdmin}
        mobileOpen={mobileOpen}
      />

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-14 shrink-0 items-center gap-3 border-b border-slate-200 bg-white px-4 dark:border-slate-800 dark:bg-slate-900">
          <button
            onClick={() => setMobileOpen(true)}
            aria-label="Open menu"
            className="rounded-md p-1.5 text-slate-500 hover:bg-slate-100 hover:text-slate-700 md:hidden dark:hover:bg-slate-800 dark:hover:text-slate-200"
          >
            ☰
          </button>
          <span className="truncate font-semibold text-slate-800 dark:text-slate-100">{headerTitle}</span>

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

          <div className="ml-auto flex items-center gap-2 sm:gap-3">
            <span className="hidden max-w-[40vw] truncate text-sm text-slate-600 sm:inline dark:text-slate-300" title={me.email ?? undefined}>
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
          <Routes>
            <Route path="/" element={<IndexRedirect projects={projects} onNewProject={() => setShowCreateProject(true)} />} />
            <Route
              path="/projects/:projectId"
              element={<Workspace actingActorId={me.actorId} members={members ?? []} />}
            />
            <Route
              path="/projects/:projectId/changes/:changeId"
              element={<Workspace actingActorId={me.actorId} members={members ?? []} />}
            />
            <Route path="/providers" element={<LlmProvidersView projectId={projectId ?? lastProjectId} />} />
            <Route path="/actors" element={<ActorsView />} />
            <Route path="/settings" element={<SiteSettingsView />} />
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </main>
      </div>

      {showCreateProject && (
        <CreateProjectModal
          onClose={() => setShowCreateProject(false)}
          onCreated={(p) => {
            setShowCreateProject(false)
            navigate(`/projects/${p.id}`)
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

function IndexRedirect({
  projects, onNewProject,
}: { projects: ReturnType<typeof useProjects>['data']; onNewProject: () => void }) {
  if (projects === undefined) {
    return <p className="text-sm text-slate-400">Loading…</p>
  }
  if (projects.length === 0) {
    return (
      <Card className="p-10 text-center text-slate-500 dark:text-slate-400">
        No projects yet —{' '}
        <button className="text-indigo-600 hover:underline" onClick={onNewProject}>create one</button>{' '}
        to get started.
      </Card>
    )
  }
  return <Navigate to={`/projects/${projects[0].id}`} replace />
}
