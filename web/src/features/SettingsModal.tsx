import { useState, type ReactNode } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { keys, useActors, useExportInstruction, useLlmProviders, useMembers, useNotificationChannels, useNotificationPreference, useProjects, useSaveNotificationChannel, useSources, useUpdateExportInstruction, useUpdateNotificationPreference } from '../api/hooks'
import type { LlmProviderConfig, Member, MemberRole, NotificationChannel, NotificationEventType } from '../api/types'
import { Badge, Button, cx, ErrorText, Field, Modal, Select, TextInput, Textarea } from '../components/ui'
import { initials } from '../lifecycle'

const ROLES: MemberRole[] = ['Reporter', 'Contributor', 'Maintainer']
// Agents can additionally take the Assistant role — a conversational capture helper with no
// lifecycle authority (used by Capture Assist Mode).
const AGENT_ROLES: MemberRole[] = ['Assistant', 'Reporter', 'Contributor', 'Maintainer']

export function SettingsModal({ projectId, onClose }: { projectId: string; onClose: () => void }) {
  const [tab, setTab] = useState<'general' | 'members' | 'sources' | 'notifications' | 'export' | 'danger'>('general')
  return (
    <Modal title="Manage project" onClose={onClose} wide>
      <div className="space-y-4">
        {/* Tabs keep the sections from competing for width, so each gets the full dialog. */}
        <div className="flex gap-1 border-b border-slate-200 dark:border-slate-800">
          <TabButton active={tab === 'general'} onClick={() => setTab('general')}>General</TabButton>
          <TabButton active={tab === 'members'} onClick={() => setTab('members')}>Members</TabButton>
          <TabButton active={tab === 'sources'} onClick={() => setTab('sources')}>Sources</TabButton>
          <TabButton active={tab === 'notifications'} onClick={() => setTab('notifications')}>Notifications</TabButton>
          <TabButton active={tab === 'export'} onClick={() => setTab('export')}>Export</TabButton>
          <TabButton active={tab === 'danger'} onClick={() => setTab('danger')}>Danger</TabButton>
        </div>
        {tab === 'general' ? <GeneralSection projectId={projectId} />
          : tab === 'members' ? <MembersSection projectId={projectId} />
            : tab === 'sources' ? <SourcesSection projectId={projectId} />
              : tab === 'notifications' ? <NotificationsSection projectId={projectId} />
                : tab === 'export' ? <ExportSection projectId={projectId} />
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
  const [sourceControlEnabled, setSourceControlEnabled] = useState(true)
  const [humanOnly, setHumanOnly] = useState(false)
  // Seed the form once the project loads, then leave it to the user.
  const [seededId, setSeededId] = useState<string | null>(null)
  if (project && project.id !== seededId) {
    setName(project.name)
    setDescription(project.description ?? '')
    setSourceControlEnabled(project.sourceControlEnabled)
    setHumanOnly(project.humanOnly)
    setSeededId(project.id)
  }

  const save = useMutation({
    mutationFn: () =>
      api.updateProject(projectId, { name: name.trim(), description: description.trim() || null, sourceControlEnabled, humanOnly }),
    onSuccess: () => qc.invalidateQueries({ queryKey: keys.projects }),
  })

  const dirty = !!project && (
    name.trim() !== project.name
    || (description.trim() || '') !== (project.description ?? '')
    || sourceControlEnabled !== project.sourceControlEnabled
    || humanOnly !== project.humanOnly
  )

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
      <div className="rounded-md border border-slate-200 p-3 dark:border-slate-700">
        <h4 className="text-xs font-semibold uppercase tracking-wide text-slate-400">Source control</h4>
        <label className="mt-2 flex items-start gap-2 text-sm text-slate-700 dark:text-slate-200">
          <input
            type="checkbox"
            className="mt-0.5"
            checked={sourceControlEnabled}
            onChange={(e) => setSourceControlEnabled(e.target.checked)}
          />
          <span>
            Enable source control
            <span className="block text-xs text-slate-400">
              When off, change requests hide their Source control section.
            </span>
          </span>
        </label>
      </div>
      <div className="rounded-md border border-slate-200 p-3 dark:border-slate-700">
        <h4 className="text-xs font-semibold uppercase tracking-wide text-slate-400">Collaboration</h4>
        <label className="mt-2 flex items-start gap-2 text-sm text-slate-700 dark:text-slate-200">
          <input
            type="checkbox"
            className="mt-0.5"
            checked={humanOnly}
            onChange={(e) => setHumanOnly(e.target.checked)}
          />
          <span>
            Human only
            <span className="block text-xs text-slate-400">
              Hide AI-agent features for this project — Capture Assist, agent commentary, adding agents,
              and agent members.
            </span>
          </span>
        </label>
      </div>
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

/// Export guidance: view/edit the work-order preamble used by the In-Development export. The backend
/// gates edits on Maintainer (or site admin); a 403 surfaces inline. Saving an empty body resets to the
/// built-in default, which the "Reset to default" action does explicitly.
function ExportSection({ projectId }: { projectId: string }) {
  const { data } = useExportInstruction(projectId)
  const update = useUpdateExportInstruction(projectId)

  // Seed the editor once the instruction loads, then leave it to the user.
  const [content, setContent] = useState('')
  const [seeded, setSeeded] = useState(false)
  if (data && !seeded) {
    setContent(data.content)
    setSeeded(true)
  }

  const dirty = !!data && content.trim() !== data.content.trim()
  const save = () => update.mutate({ content }, { onSuccess: (res) => setContent(res.content) })
  const reset = () => {
    if (!window.confirm('Reset the export guidance to the built-in default? Your custom text will be removed.')) return
    update.mutate({ content: '' }, { onSuccess: (res) => setContent(res.content) })
  }

  return (
    <section className="space-y-3">
      <p className="text-sm text-slate-600 dark:text-slate-400">
        The guidance inserted into the In-Development export between the work-order title and the change
        list. The project name and the change list are generated automatically.
        {data?.isDefault && (
          <span className="block text-xs text-slate-400">
            Using the built-in default — saving an edit creates a project-specific override.
          </span>
        )}
      </p>
      <Field label="Export guidance (Markdown)">
        <Textarea
          value={content}
          onChange={(e) => setContent(e.target.value)}
          rows={16}
          className="font-mono text-xs"
          placeholder="Markdown guidance for the export work order…"
        />
      </Field>
      <ErrorText error={update.error} />
      <div className="flex justify-between">
        <Button variant="subtle" disabled={!data || data.isDefault || update.isPending} onClick={reset}>
          Reset to default
        </Button>
        <Button variant="primary" disabled={!content.trim() || !dirty || update.isPending} onClick={save}>
          {update.isPending ? 'Saving…' : 'Save changes'}
        </Button>
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
  const { data: projects } = useProjects(true, true)
  // Human-only projects hide agents: agent members aren't listed and the add-agent form is gone.
  const humanOnly = projects?.find((p) => p.id === projectId)?.humanOnly ?? false
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
        {(members ?? []).filter((m) => !humanOnly || m.type !== 'Agent').map((m) => (
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
              <div className="flex flex-col gap-2 sm:flex-row sm:items-end">
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

          {/* Create an agent member — hidden for Human-Only projects. */}
          {!humanOnly && (
            <div className="space-y-2 border-t border-slate-200 pt-3 dark:border-slate-700">
              <h5 className="text-xs font-medium text-slate-500 dark:text-slate-400">Agent</h5>
              <Field label="Display name">
                <TextInput value={agentName} onChange={(e) => setAgentName(e.target.value)} placeholder="Aria" />
              </Field>
              <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
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
          )}
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

// Only the events that actually fire this pass are offered (the type union declares more for the future).
const SELECTABLE_EVENTS: { value: NotificationEventType; label: string; hint: string }[] = [
  { value: 'AwaitingApproval', label: 'Awaiting approval', hint: 'A change enters Triaged and awaits a Maintainer.' },
  { value: 'AssignedToYou', label: 'Assigned to you', hint: 'A change is assigned to a member.' },
  { value: 'WorkOrderResults', label: 'Work-order results', hint: 'A coding agent reports back on an export.' },
  { value: 'DailyDigest', label: 'Daily digest', hint: 'A once-a-day per-member summary.' },
]

const EVENT_LABEL: Record<string, string> = Object.fromEntries(SELECTABLE_EVENTS.map((e) => [e.value, e.label]))

/// Outbound notifications: per-project Google Chat channels (which events flow to which space) plus the
/// current user's own digest opt-out. Channel management is gated on Maintainer/site-admin by the backend;
/// a 403 surfaces inline. The digest opt-out is the caller's own preference, so any member may set it.
function NotificationsSection({ projectId }: { projectId: string }) {
  const { data: channels } = useNotificationChannels(projectId)
  const { data: preference } = useNotificationPreference(projectId)
  const updatePreference = useUpdateNotificationPreference(projectId)
  const { create, update, remove } = useSaveNotificationChannel(projectId)

  const [editing, setEditing] = useState<NotificationChannel | null>(null)
  const [creating, setCreating] = useState(false)

  const onDelete = (c: NotificationChannel) => {
    if (!window.confirm(`Delete the notification channel "${c.name}"? Its pending deliveries are dropped.`)) return
    remove.mutate(c.id)
  }

  return (
    <section className="space-y-4">
      {/* The caller's own digest opt-out — a personal preference, not project config. */}
      <label className="flex items-start gap-2 rounded-md border border-slate-200 p-3 text-sm text-slate-700 dark:border-slate-700 dark:text-slate-200">
        <input
          type="checkbox"
          className="mt-0.5"
          checked={preference?.digestOptOut ?? false}
          disabled={!preference || updatePreference.isPending}
          onChange={(e) => updatePreference.mutate({ digestOptOut: e.target.checked })}
        />
        <span>
          Exclude me from the daily digest
          <span className="block text-xs text-slate-400">
            The digest still sends for this project; your section is left out of it.
          </span>
        </span>
      </label>

      <div>
        <div className="mb-2 flex items-center justify-between">
          <h4 className="text-xs font-semibold uppercase tracking-wide text-slate-400">Channels</h4>
          {!creating && !editing && (
            <Button variant="subtle" onClick={() => { setCreating(true); setEditing(null) }}>Add channel</Button>
          )}
        </div>
        <div className="divide-y divide-slate-100 overflow-hidden rounded-md border border-slate-200 dark:divide-slate-800 dark:border-slate-700">
          {(channels ?? []).map((c) => (
            <div key={c.id} className="flex items-start justify-between gap-3 px-3 py-2 text-sm">
              <div className="min-w-0 space-y-1">
                <div className="flex flex-wrap items-center gap-1.5">
                  <span className="text-slate-700 dark:text-slate-200">{c.name}</span>
                  <span className="text-slate-400">· Google Chat</span>
                  {c.enabled ? <Badge tone="green">Enabled</Badge> : <Badge tone="slate">Disabled</Badge>}
                  <ChannelHealth channel={c} />
                </div>
                <div className="text-xs text-slate-500 dark:text-slate-400">
                  <code className="font-mono">{c.target}</code>
                </div>
                <div className="flex flex-wrap gap-1">
                  {c.events.length === 0
                    ? <span className="text-xs text-slate-400">No events</span>
                    : c.events.map((e) => <Badge key={e} tone="indigo">{EVENT_LABEL[e] ?? e}</Badge>)}
                </div>
              </div>
              <div className="flex shrink-0 gap-1">
                <Button variant="subtle" onClick={() => { setEditing(c); setCreating(false) }}>Edit</Button>
                <Button variant="subtle" disabled={remove.isPending} onClick={() => onDelete(c)}>Delete</Button>
              </div>
            </div>
          ))}
          {channels?.length === 0 && <p className="px-3 py-4 text-sm text-slate-400">No channels configured.</p>}
        </div>
        <ErrorText error={remove.error} />
      </div>

      {(creating || editing) && (
        <NotificationChannelForm
          key={editing?.id ?? 'new'}
          initial={editing}
          pending={create.isPending || update.isPending}
          error={editing ? update.error : create.error}
          onCancel={() => { setCreating(false); setEditing(null) }}
          onSubmit={(body) => {
            if (editing) {
              update.mutate({ id: editing.id, body }, { onSuccess: () => setEditing(null) })
            } else {
              // Create has no `enabled` (new channels are enabled by default); pass only its fields.
              create.mutate(
                { type: body.type, name: body.name, target: body.target, secretRef: body.secretRef, events: body.events },
                { onSuccess: () => setCreating(false) },
              )
            }
          }}
        />
      )}
    </section>
  )
}

// The last-delivery health badge — makes a dead endpoint visible instead of silently failing.
function ChannelHealth({ channel }: { channel: NotificationChannel }) {
  if (!channel.lastDeliveryAt) return <Badge tone="slate">Never delivered</Badge>
  if (channel.lastDeliveryOk) return <Badge tone="green">Delivered</Badge>
  return (
    <span title={channel.lastDeliveryError ?? undefined}>
      <Badge tone="red">Last delivery failed</Badge>
    </span>
  )
}

function NotificationChannelForm({
  initial, pending, error, onSubmit, onCancel,
}: {
  initial: NotificationChannel | null
  pending: boolean
  error: unknown
  onSubmit: (body: { type: 'GoogleChat'; name: string; target: string; secretRef: string | null; events: NotificationEventType[]; enabled: boolean }) => void
  onCancel: () => void
}) {
  const [name, setName] = useState(initial?.name ?? '')
  const [target, setTarget] = useState(initial?.target ?? '')
  const [secretRef, setSecretRef] = useState(initial?.secretRef ?? '')
  const [enabled, setEnabled] = useState(initial?.enabled ?? true)
  const [events, setEvents] = useState<Set<NotificationEventType>>(new Set(initial?.events ?? []))

  const toggleEvent = (value: NotificationEventType) =>
    setEvents((prev) => {
      const next = new Set(prev)
      if (next.has(value)) next.delete(value); else next.add(value)
      return next
    })

  const submit = () =>
    onSubmit({
      type: 'GoogleChat',
      name: name.trim(),
      target: target.trim(),
      secretRef: secretRef.trim() || null,
      events: [...events],
      enabled,
    })

  return (
    <div className="space-y-3 rounded-md border border-slate-200 p-3 dark:border-slate-700">
      <h4 className="text-xs font-semibold uppercase tracking-wide text-slate-400">
        {initial ? 'Edit channel' : 'Add Google Chat channel'}
      </h4>
      <Field label="Name"><TextInput value={name} onChange={(e) => setName(e.target.value)} placeholder="Team space" /></Field>
      <Field label="Google Chat space">
        <TextInput value={target} onChange={(e) => setTarget(e.target.value)} placeholder="spaces/AAAAAAAAAAA" />
      </Field>
      <Field label="Credentials secret reference">
        <TextInput value={secretRef} onChange={(e) => setSecretRef(e.target.value)} placeholder="e.g. GCHAT_CREDS" />
      </Field>
      <p className="text-xs text-slate-400">
        A lookup key, not the credentials themselves — you must separately set its value (the service-account
        JSON) in configuration (<code className="font-mono">Secrets:{secretRef.trim() || '<name>'}</code>) or an
        environment variable of the same name. Required, because Google Chat can't authenticate without it.
      </p>
      <div>
        <div className="mb-1 text-xs font-semibold uppercase tracking-wide text-slate-400">Events</div>
        <div className="space-y-1.5">
          {SELECTABLE_EVENTS.map((e) => (
            <label key={e.value} className="flex items-start gap-2 text-sm text-slate-700 dark:text-slate-200">
              <input type="checkbox" className="mt-0.5" checked={events.has(e.value)} onChange={() => toggleEvent(e.value)} />
              <span>{e.label}<span className="block text-xs text-slate-400">{e.hint}</span></span>
            </label>
          ))}
        </div>
      </div>
      {initial && (
        <label className="flex items-center gap-2 text-sm text-slate-700 dark:text-slate-200">
          <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} />
          Enabled
        </label>
      )}
      <ErrorText error={error} />
      <div className="flex justify-end gap-2">
        <Button variant="subtle" onClick={onCancel}>Cancel</Button>
        <Button variant="primary" disabled={!name.trim() || !target.trim() || !secretRef.trim() || pending} onClick={submit}>
          {pending ? 'Saving…' : initial ? 'Save changes' : 'Add channel'}
        </Button>
      </div>
    </div>
  )
}

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
          <div key={s.id} className="flex items-center justify-between gap-3 px-3 py-2 text-sm">
            <div className="min-w-0">
              <div className="text-slate-700 dark:text-slate-200">
                {s.name} · <span className="text-slate-400">{s.type}</span>
              </div>
              {/* The GUID below is the SDK's `sourceId` — surfaced here so it's copyable without
                  digging through the API or DB. */}
              <div className="mt-0.5 flex items-center gap-1.5">
                <span className="text-[10px] font-semibold uppercase tracking-wide text-slate-400">ID</span>
                <code className="break-all font-mono text-xs text-slate-500 dark:text-slate-400">{s.id}</code>
              </div>
            </div>
            <CopyButton value={s.id} />
          </div>
        ))}
        {sources?.length === 0 && <p className="px-3 py-4 text-sm text-slate-400">None configured.</p>}
      </div>
      <div className="flex flex-col gap-2 rounded-md border border-slate-200 p-3 sm:flex-row sm:items-end dark:border-slate-700">
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

// Copies a value (the source's `sourceId`) to the clipboard, flashing confirmation briefly.
function CopyButton({ value }: { value: string }) {
  const [copied, setCopied] = useState(false)
  return (
    <Button
      variant="subtle"
      className="shrink-0"
      onClick={async () => {
        try {
          await navigator.clipboard.writeText(value)
          setCopied(true)
          setTimeout(() => setCopied(false), 1500)
        } catch { /* clipboard blocked (e.g. insecure context) — the ID is still visible to copy by hand */ }
      }}
    >
      {copied ? 'Copied' : 'Copy ID'}
    </Button>
  )
}
