import { useEffect, useState } from 'react'
import { useMembers, useProjects } from './api/hooks'
import { Button, Card, Select } from './components/ui'
import { Workspace } from './features/Workspace'
import { SettingsModal } from './features/SettingsModal'
import { CreateProjectModal } from './features/CreateProjectModal'

export default function App() {
  const { data: projects } = useProjects()
  const [projectId, setProjectId] = useState<string>()
  const [actingActorId, setActingActorId] = useState<string>()
  const [showSettings, setShowSettings] = useState(false)
  const [showCreateProject, setShowCreateProject] = useState(false)

  useEffect(() => {
    if (!projectId && projects && projects.length > 0) setProjectId(projects[0].id)
  }, [projects, projectId])

  const { data: members } = useMembers(projectId)
  useEffect(() => {
    if (members && members.length > 0 && !members.some((m) => m.actorId === actingActorId)) {
      setActingActorId(members[0].actorId)
    }
  }, [members, actingActorId])

  return (
    <div className="min-h-screen">
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex max-w-7xl flex-wrap items-center gap-3 px-6 py-3">
          <span className="text-lg font-semibold tracking-tight text-indigo-700">Dimes</span>
          <span className="text-sm text-slate-400">change tracker</span>

          <div className="ml-4 flex items-center gap-2">
            <Select
              value={projectId ?? ''}
              onChange={(e) => setProjectId(e.target.value || undefined)}
              className="min-w-44"
            >
              {(projects ?? []).length === 0 && <option value="">No projects yet</option>}
              {(projects ?? []).map((p) => (
                <option key={p.id} value={p.id}>{p.name}</option>
              ))}
            </Select>
            <Button variant="subtle" onClick={() => setShowCreateProject(true)}>+ Project</Button>
          </div>

          <div className="ml-auto flex items-center gap-2">
            <span className="text-xs text-slate-500">Acting as</span>
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
            <Button variant="default" onClick={() => setShowSettings(true)} disabled={!projectId}>Manage</Button>
          </div>
        </div>
      </header>

      <main className="mx-auto max-w-7xl px-6 py-6">
        {projectId && actingActorId ? (
          <Workspace projectId={projectId} actingActorId={actingActorId} members={members ?? []} />
        ) : (
          <Card className="p-10 text-center text-slate-500">
            Create a project and add a member to get started.
          </Card>
        )}
      </main>

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
