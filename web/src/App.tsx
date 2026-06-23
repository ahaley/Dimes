import { useEffect, useMemo, useState } from 'react'
import { Navigate, Route, Routes, useLocation, useMatch, useNavigate } from 'react-router-dom'
import { api } from './api/client'
import { useMe, useMembers, useMyAssignmentCounts, useProjects } from './api/hooks'
import { useProjectsLiveUpdates } from './api/realtime'
import { Button, Card } from './components/ui'
import { Sidebar } from './features/Sidebar'
import { Workspace } from './features/Workspace'
import { FocusView } from './features/FocusView'
import { LlmProvidersView } from './features/LlmProvidersView'
import { ActorsView } from './features/ActorsView'
import { ActorDetailView } from './features/ActorDetailView'
import { CaptureAssistView } from './features/CaptureAssistView'
import { AssistConversationView } from './features/AssistConversationView'
import { SettingsModal } from './features/SettingsModal'
import { CreateProjectModal } from './features/CreateProjectModal'
import { LoginView } from './features/LoginView'
import { SiteSettingsView } from './features/SiteSettingsView'
import { applyTheme, getInitialTheme, type Theme } from './theme'

const COLLAPSE_KEY = 'dimes.sidebar.collapsed'

// Per-project baseline of assignment counts the user has already seen — the sidebar badge shows only
// assignments new since the project's board was last viewed. Stored per-browser like the collapse/theme
// prefs; shape is { [projectId]: countSeen }.
const SEEN_KEY = 'dimes.assignmentsSeen'
function readSeenAssignments(): Record<string, number> {
  try {
    const parsed = JSON.parse(localStorage.getItem(SEEN_KEY) ?? '{}')
    return parsed && typeof parsed === 'object' ? (parsed as Record<string, number>) : {}
  } catch {
    return {}
  }
}
function writeSeenAssignments(seen: Record<string, number>): void {
  localStorage.setItem(SEEN_KEY, JSON.stringify(seen))
}

type View = 'board' | 'providers' | 'actors' | 'settings'

export default function App() {
  const { data: me, isLoading: meLoading, isError: loggedOut } = useMe()
  // Include archived so the sidebar can surface them in a separate group; split for the active list.
  // Keep activeProjects undefined while loading so IndexRedirect can distinguish "loading" from "none".
  const { data: projects } = useProjects(!!me, true)
  const activeProjects = projects?.filter((p) => !p.isArchived)
  const archivedProjects = projects?.filter((p) => p.isArchived) ?? []
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

  // Per-project count of change requests assigned to me, for the sidebar "assigned to you" indicator.
  const { data: assignmentCounts } = useMyAssignmentCounts(!!me)
  // Stable identity so the seen-tracking effects below don't re-run every render.
  const rawCounts = useMemo(
    () => new Map((assignmentCounts ?? []).map((a) => [a.projectId, a.count])),
    [assignmentCounts],
  )
  // "Seen" baseline per project; the badge surfaces only assignments new since the project was viewed.
  const [seen, setSeen] = useState<Record<string, number>>(readSeenAssignments)

  // When assignments leave a project (completed/reassigned), clamp its baseline down so a later new
  // assignment still surfaces. Returns the previous object unchanged when nothing clamps (no render loop).
  useEffect(() => {
    setSeen((prev) => {
      let next = prev
      for (const [pid, c] of rawCounts) {
        if (prev[pid] != null && prev[pid] > c) {
          if (next === prev) next = { ...prev }
          next[pid] = c
        }
      }
      if (next === prev) return prev
      writeSeenAssignments(next)
      return next
    })
  }, [rawCounts])

  // Viewing a project (any /projects/:id route) marks its current count as seen, clearing its badge.
  const activeCount = projectId ? rawCounts.get(projectId) ?? 0 : 0
  useEffect(() => {
    if (!projectId) return
    setSeen((prev) => {
      if (prev[projectId] === activeCount) return prev
      const next = { ...prev, [projectId]: activeCount }
      writeSeenAssignments(next)
      return next
    })
  }, [projectId, activeCount])

  // Badge value = assignments new since the project's board was last viewed (hidden when zero).
  const assignmentBadgeByProject = useMemo(() => {
    const m = new Map<string, number>()
    for (const [pid, c] of rawCounts) {
      const delta = c - Math.min(seen[pid] ?? 0, c)
      if (delta > 0) m.set(pid, delta)
    }
    return m
  }, [rawCounts, seen])

  // Keep the sidebar project list live (create / archive / unarchive from any client).
  useProjectsLiveUpdates(!!me)

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

  // Managing a project (members, sources, details, archive) is a Maintainer/site-admin action — the
  // backend enforces it; hide the affordance for everyone below that.
  const myRole = (members ?? []).find((m) => m.actorId === me.actorId)?.role
  const canManage = me.isSiteAdmin || myRole === 'Maintainer'

  const view: View =
    location.pathname.startsWith('/providers') ? 'providers'
      : location.pathname.startsWith('/actors') ? 'actors'
        : location.pathname.startsWith('/settings') ? 'settings'
          : 'board'

  const headerTitle =
    view === 'providers' ? 'LLM providers'
      : view === 'actors' ? 'Actors'
        : view === 'settings' ? 'Site settings'
          : (currentProject ? `${currentProject.key ? `${currentProject.key} · ` : ''}${currentProject.name}` : 'Dimes')

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
        projects={activeProjects ?? []}
        archivedProjects={archivedProjects}
        assignmentCounts={assignmentBadgeByProject}
        projectId={projectId}
        onSelect={(id) => navigate(`/projects/${id}`)}
        collapsed={collapsed}
        onToggleCollapse={toggleCollapsed}
        canCreateProject={me.isSiteAdmin}
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

          {view === 'board' && projectId && canManage && (
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
            <Route path="/" element={<IndexRedirect projects={activeProjects} canCreate={me.isSiteAdmin} onNewProject={() => setShowCreateProject(true)} />} />
            <Route
              path="/projects/:projectId"
              element={<Workspace actingActorId={me.actorId} members={members ?? []} />}
            />
            <Route
              path="/projects/:projectId/changes/:changeId"
              element={<Workspace actingActorId={me.actorId} members={members ?? []} />}
            />
            {/* Capture Assist is an AI-agent feature — blocked for Human-Only projects (guards bookmarks). */}
            <Route
              path="/projects/:projectId/capture"
              element={currentProject?.humanOnly ? <Navigate to={`/projects/${projectId}`} replace /> : <CaptureAssistView />}
            />
            <Route
              path="/projects/:projectId/capture/:conversationId"
              element={currentProject?.humanOnly ? <Navigate to={`/projects/${projectId}`} replace /> : <CaptureAssistView />}
            />
            <Route path="/projects/:projectId/assist/:conversationId" element={<AssistConversationView />} />
            <Route
              path="/projects/:projectId/focus/:status"
              element={<FocusView actingActorId={me.actorId} members={members ?? []} />}
            />
            {/* LLM providers + Actors are site-admin-only; guard the routes so a bookmark can't reach them. */}
            <Route path="/providers" element={me.isSiteAdmin ? <LlmProvidersView projectId={projectId ?? lastProjectId} /> : <Navigate to="/" replace />} />
            <Route path="/actors" element={me.isSiteAdmin ? <ActorsView /> : <Navigate to="/" replace />} />
            <Route path="/actors/:actorId" element={me.isSiteAdmin ? <ActorDetailView /> : <Navigate to="/" replace />} />
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
      {showSettings && projectId && canManage && (
        <SettingsModal projectId={projectId} onClose={() => setShowSettings(false)} />
      )}
    </div>
  )
}

function IndexRedirect({
  projects, canCreate, onNewProject,
}: { projects: ReturnType<typeof useProjects>['data']; canCreate: boolean; onNewProject: () => void }) {
  if (projects === undefined) {
    return <p className="text-sm text-slate-400">Loading…</p>
  }
  if (projects.length === 0) {
    return (
      <Card className="p-10 text-center text-slate-500 dark:text-slate-400">
        {canCreate ? (
          <>
            No projects yet —{' '}
            <button className="text-indigo-600 hover:underline" onClick={onNewProject}>create one</button>{' '}
            to get started.
          </>
        ) : (
          <>No projects yet — ask a site administrator to create one and add you to it.</>
        )}
      </Card>
    )
  }
  return <Navigate to={`/projects/${projects[0].id}`} replace />
}
