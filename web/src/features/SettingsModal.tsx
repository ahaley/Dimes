import { useState, type ReactNode } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { keys, useActors, useLlmProviders, useMembers, useProjects, useSources } from '../api/hooks'
import type { LlmProviderConfig, Member, MemberRole } from '../api/types'
import { Badge, Button, cx, ErrorText, Field, Modal, Select, TextInput, Textarea } from '../components/ui'
import { initials } from '../lifecycle'

const ROLES: MemberRole[] = ['Reporter', 'Contributor', 'Maintainer']
// Agents can additionally take the Assistant role — a conversational capture helper with no
// lifecycle authority (used by Capture Assist Mode).
const AGENT_ROLES: MemberRole[] = ['Assistant', 'Reporter', 'Contributor', 'Maintainer']

export function SettingsModal({ projectId, onClose }: { projectId: string; onClose: () => void }) {
  const [tab, setTab] = useState<'general' | 'members' | 'sources' | 'danger'>('general')
  return (
    <Modal title="Manage project" onClose={onClose} wide>
      <div className="space-y-4">
        {/* Tabs keep the sections from competing for width, so each gets the full dialog. */}
        <div className="flex gap-1 border-b border-slate-200 dark:border-slate-800">
          <TabButton active={tab === 'general'} onClick={() => setTab('general')}>General</TabButton>
          <TabButton active={tab === 'members'} onClick={() => setTab('members')}>Members</TabButton>
          <TabButton active={tab === 'sources'} onClick={() => setTab('sources')}>Sources</TabButton>
          <TabButton active={tab === 'danger'} onClick={() => setTab('danger')}>Danger</TabButton>
        </div>
        {tab === 'general' ? <GeneralSection projectId={projectId} />
          : tab === 'members' ? <MembersSection projectId={projectId} />
            : tab === 'sources' ? <SourcesSection projectId={projectId} />
              : <DangerSection projectId={projectId} />}
      </div>
    </Modal>
  )
}

/// Project identity: rename and edit the description. The backend gates this on Maintainer (or site
/// admin); a 403 surfaces inline for anyone below that.
function GeneralSection({ projectId }: { projectId: string }) {
  const qc = useQueryClient()
  const { data: projects } = useProjects(true, true)
  const project = projects?.find((p) => p.id === projectId)

  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  // Seed the form once the project loads, then leave it to the user.
  const [seededId, setSeededId] = useState<string | null>(null)
  if (project && project.id !== seededId) {
    setName(project.name)
    setDescription(project.description ?? '')
    setSeededId(project.id)
  }

  const save = useMutation({
    mutationFn: () => api.updateProject(projectId, { name: name.trim(), description: description.trim() || null }),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.projects }),
  })

  const dirty = !!project && (name.trim() !== project.name || (description.trim() || '') !== (project.description ?? ''))

  return (
    <section className="space-y-3">
      <Field label="Name">
        <TextInput value={name} onChange={(e) => setName(e.target.value)} placeholder="Project name" />
      </Field>
      <Field label="Description">
        <Textarea
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          placeholder="What this project is about…"
        />
      </Field>
      <ErrorText error={save.error} />
      <div className="flex justify-end">
        <Button variant="primary" disabled={!name.trim() || !dirty || save.isPending} onClick={() => save.mutate()}>
          {save.isPending ? 'Saving…' : 'Save changes'}
        </Button>
      </div>
    </section>
  )
}

/// Project-level danger zone: archive (soft-delete) or unarchive. The backend enforces that only a
/// project Maintainer or site admin can do this; a 403 surfaces inline if the caller lacks authority.
function DangerSection({ projectId }: { projectId: string }) {
  const qc = useQueryClient()
  const { data: projects } = useProjects(true, true)
  const project = projects?.find((p) => p.id === projectId)
  const archived = project?.isArchived ?? false

  const toggle = useMutation({
    mutationFn: () => (archived ? api.unarchiveProject(projectId) : api.archiveProject(projectId)),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.projects }),
  })

  return (
    <section className="space-y-3">
      <div className="rounded-md border border-red-200 bg-red-50/60 p-4 dark:border-red-900/50 dark:bg-red-950/20">
        <h4 className="text-sm font-semibold text-red-700 dark:text-red-300">
          {archived ? 'Unarchive project' : 'Archive project'}
        </h4>
        <p className="mt-1 text-sm text-slate-600 dark:text-slate-400">
          {archived
            ? 'This project is archived and hidden from the active list. Unarchiving restores it.'
            : 'Archiving hides this project from the active list. Its changes, observations, and history are kept, and it can be unarchived later.'}
        </p>
        <ErrorText error={toggle.error} />
        <div className="mt-3">
          <Button
            variant={archived ? 'primary' : 'danger'}
            disabled={toggle.isPending}
            onClick={() => {
              if (archived) { toggle.mutate(); return }
              if (window.confirm(`Archive ${project?.name ?? 'this project'}? It will be hidden from the active list. You can unarchive it later.`)) {
                toggle.mutate()
              }
            }}
          >
            {archived ? 'Unarchive project' : 'Archive project'}
          </Button>
        </div>
      </div>
    </section>
  )
}

function TabButton({ active, onClick, children }: { active: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className={cx(
        '-mb-px border-b-2 px-3 py-2 text-sm font-medium transition-colors',
        active
          ? 'border-indigo-500 text-slate-800 dark:text-slate-100'
          : 'border-transparent text-slate-400 hover:text-slate-600 dark:hover:text-slate-200',
      )}
    >
      {children}
    </button>
  )
}

function Avatar({ name }: { name: string }) {
  return (
    <div className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-slate-100 text-xs font-semibold text-slate-600 dark:bg-slate-800 dark:text-slate-300">
      {initials(name)}
    </div>
  )
}

function MembersSection({ projectId }: { projectId: string }) {
  const qc = useQueryClient()
  const { data: members } = useMembers(projectId)
  const { data: providers } = useLlmProviders(projectId)
  const { data: actors } = useActors(false)
  const invalidate = () => qc.invalidateQueries({ queryKey: keys.members(projectId) })

  // The add forms are hidden behind a disclosure so the member list reads clearly by default.
  const [adding, setAdding] = useState(false)

  // Add person: link an existing site user (no new actor).
  const [userId, setUserId] = useState('')
  const [personRole, setPersonRole] = useState<MemberRole>('Contributor')
  const memberIds = new Set((members ?? []).map((m) => m.actorId))
  const candidates = (actors ?? []).filter((a) => a.type === 'Human' && !a.isArchived && !memberIds.has(a.id))
  const assignPerson = useMutation({
    mutationFn: () => api.assignMember(projectId, userId, { role: personRole }),
    onSuccess: () => { invalidate(); setUserId('') },
  })

  // Add agent: created inline (agents aren't site users).
  const [agentName, setAgentName] = useState('')
  const [agentRole, setAgentRole] = useState<MemberRole>('Contributor')
  const [llmProviderConfigId, setLlm] = useState('')
  const addAgent = useMutation({
    mutationFn: () =>
      api.addMember(projectId, {
        displayName: agentName,
        type: 'Agent',
        role: agentRole,
        email: null,
        llmProviderConfigId: llmProviderConfigId || null,
      }),
    onSuccess: () => { invalidate(); setAgentName('') },
  })

  return (
    <section className="space-y-4">
      <div className="divide-y divide-slate-100 overflow-hidden rounded-md border border-slate-200 dark:divide-slate-800 dark:border-slate-700">
        {(members ?? []).map((m) => (
          <MemberRow key={m.actorId} projectId={projectId} member={m} providers={providers ?? []} />
        ))}
        {members?.length === 0 && <p className="px-3 py-4 text-sm text-slate-400">No members yet.</p>}
      </div>

      {!adding ? (
        <Button variant="default" onClick={() => setAdding(true)}>+ Add member</Button>
      ) : (
        <div className="space-y-4 rounded-md border border-slate-200 p-3 dark:border-slate-700">
          <div className="flex items-center justify-between">
            <h4 className="text-xs font-semibold uppercase tracking-wide text-slate-400">Add member</h4>
            <Button variant="subtle" onClick={() => setAdding(false)}>Done</Button>
          </div>

          {/* Add an existing user as a member */}
          <div className="space-y-2">
            <h5 className="text-xs font-medium text-slate-500 dark:text-slate-400">Person</h5>
            {candidates.length === 0 ? (
              <p className="text-sm text-slate-400">No users available — create users in Site settings.</p>
            ) : (
              <div className="flex items-end gap-2">
                <Field label="User">
                  <Select value={userId} onChange={(e) => setUserId(e.target.value)}>
                    <option value="">Select…</option>
                    {candidates.map((a) => (
                      <option key={a.id} value={a.id}>{a.displayName}{a.email ? ` (${a.email})` : ''}</option>
                    ))}
                  </Select>
                </Field>
                <Field label="Role">
                  <Select value={personRole} onChange={(e) => setPersonRole(e.target.value as MemberRole)}>
                    {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
                  </Select>
                </Field>
                <Button variant="primary" disabled={!userId || assignPerson.isPending} onClick={() => assignPerson.mutate()}>
                  Add
                </Button>
              </div>
            )}
            <ErrorText error={assignPerson.error} />
          </div>

          {/* Create an agent member */}
          <div className="space-y-2 border-t border-slate-200 pt-3 dark:border-slate-700">
            <h5 className="text-xs font-medium text-slate-500 dark:text-slate-400">Agent</h5>
            <Field label="Display name">
              <TextInput value={agentName} onChange={(e) => setAgentName(e.target.value)} placeholder="Aria" />
            </Field>
            <div className="grid grid-cols-2 gap-2">
              <Field label="Role">
                <Select value={agentRole} onChange={(e) => setAgentRole(e.target.value as MemberRole)}>
                  {AGENT_ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
                </Select>
              </Field>
              <Field label="LLM provider (for commentary)">
                <Select value={llmProviderConfigId} onChange={(e) => setLlm(e.target.value)}>
                  <option value="">none</option>
                  {(providers ?? []).map((p) => (
                    <option key={p.id} value={p.id}>{p.name} ({p.model})</option>
                  ))}
                </Select>
              </Field>
            </div>
            <ErrorText error={addAgent.error} />
            <Button variant="primary" disabled={!agentName.trim() || addAgent.isPending} onClick={() => addAgent.mutate()}>
              Add agent
            </Button>
          </div>
        </div>
      )}
    </section>
  )
}

function MemberRow({
  projectId, member, providers,
}: { projectId: string; member: Member; providers: LlmProviderConfig[] }) {
  const qc = useQueryClient()
  const invalidate = () => qc.invalidateQueries({ queryKey: keys.members(projectId) })
  const [editing, setEditing] = useState(false)
  const [displayName, setDisplayName] = useState(member.displayName)
  const [role, setRole] = useState<MemberRole>(member.role)
  const [llm, setLlm] = useState(member.llmProviderConfigId ?? '')

  // Humans: identity is managed in Site settings, so the project panel only changes the role (link).
  const changeRole = useMutation({
    mutationFn: (r: MemberRole) => api.assignMember(projectId, member.actorId, { role: r }),
    onSuccess: invalidate,
  })
  // Agents: edit identity + role + LLM binding inline.
  const save = useMutation({
    mutationFn: () =>
      api.updateMember(projectId, member.actorId, {
        displayName,
        email: member.email ?? null,
        role,
        llmProviderConfigId: member.type === 'Agent' && llm ? llm : null,
      }),
    onSuccess: () => { invalidate(); setEditing(false) },
  })
  const remove = useMutation({
    mutationFn: () => api.removeMember(projectId, member.actorId),
    onSuccess: invalidate,
  })

  const removeButton = (
    <Button
      variant="subtle"
      disabled={remove.isPending}
      onClick={() => { if (window.confirm(`Remove ${member.displayName} from this project?`)) remove.mutate() }}
    >
      Remove
    </Button>
  )

  if (member.type === 'Human') {
    return (
      <div className="flex items-center gap-3 px-3 py-2 text-sm">
        <Avatar name={member.displayName} />
        <div className="min-w-0 flex-1">
          <div className="truncate text-slate-800 dark:text-slate-100">{member.displayName}</div>
          {member.email && <div className="truncate text-xs text-slate-400">{member.email}</div>}
        </div>
        <Badge tone="slate">Person</Badge>
        <Select
          value={member.role}
          className="max-w-36"
          disabled={changeRole.isPending}
          onChange={(e) => changeRole.mutate(e.target.value as MemberRole)}
        >
          {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
        </Select>
        {removeButton}
      </div>
    )
  }

  if (!editing) {
    return (
      <div className="flex items-center gap-3 px-3 py-2 text-sm">
        <Avatar name={member.displayName} />
        <span className="min-w-0 flex-1 truncate text-slate-800 dark:text-slate-100">{member.displayName}</span>
        <Badge tone="violet">Agent</Badge>
        <Badge tone={member.role === 'Maintainer' ? 'indigo' : 'slate'}>{member.role}</Badge>
        <Button variant="subtle" onClick={() => setEditing(true)}>Edit</Button>
        {removeButton}
      </div>
    )
  }

  return (
    <div className="space-y-2 px-3 py-3">
      <Field label="Display name">
        <TextInput value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
      </Field>
      <Field label="Role">
        <Select value={role} onChange={(e) => setRole(e.target.value as MemberRole)}>
          {AGENT_ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
        </Select>
      </Field>
      <Field label="LLM provider (for commentary)">
        <Select value={llm} onChange={(e) => setLlm(e.target.value)}>
          <option value="">none</option>
          {providers.map((p) => <option key={p.id} value={p.id}>{p.name} ({p.model})</option>)}
        </Select>
      </Field>
      <ErrorText error={save.error ?? remove.error} />
      <div className="flex justify-end gap-2">
        <Button variant="subtle" onClick={() => setEditing(false)}>Cancel</Button>
        <Button variant="primary" disabled={!displayName.trim() || save.isPending} onClick={() => save.mutate()}>Save</Button>
      </div>
    </div>
  )
}

// The UI only creates external capture sources; the Internal source type is provisioned server-side.
type CreatableSourceType = 'Sdk' | 'Seq'

function SourcesSection({ projectId }: { projectId: string }) {
  const qc = useQueryClient()
  const { data: sources } = useSources(projectId)
  const [type, setType] = useState<CreatableSourceType>('Sdk')
  const [name, setName] = useState('')

  const add = useMutation({
    mutationFn: () => api.createSource(projectId, { type, name, configJson: null }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: keys.sources(projectId) })
      setName('')
    },
  })

  return (
    <section className="space-y-3">
      <div className="divide-y divide-slate-100 overflow-hidden rounded-md border border-slate-200 dark:divide-slate-800 dark:border-slate-700">
        {(sources ?? []).map((s) => (
          <div key={s.id} className="px-3 py-2 text-sm text-slate-700 dark:text-slate-200">
            {s.name} · <span className="text-slate-400">{s.type}</span>
          </div>
        ))}
        {sources?.length === 0 && <p className="px-3 py-4 text-sm text-slate-400">None configured.</p>}
      </div>
      <div className="flex items-end gap-2 rounded-md border border-slate-200 p-3 dark:border-slate-700">
        <Field label="Type">
          <Select value={type} onChange={(e) => setType(e.target.value as CreatableSourceType)}>
            <option value="Sdk">SDK</option>
            <option value="Seq">Seq</option>
          </Select>
        </Field>
        <Field label="Name"><TextInput value={name} onChange={(e) => setName(e.target.value)} placeholder="web-sdk" /></Field>
        <Button variant="primary" disabled={!name.trim() || add.isPending} onClick={() => add.mutate()}>Add</Button>
      </div>
      <ErrorText error={add.error} />
    </section>
  )
}
