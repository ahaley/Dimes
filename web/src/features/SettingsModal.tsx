import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { keys, useActors, useLlmProviders, useMembers, useSources } from '../api/hooks'
import type { LlmProviderConfig, Member, MemberRole, ObservationSourceType } from '../api/types'
import { Badge, Button, ErrorText, Field, Modal, Select, TextInput } from '../components/ui'

const ROLES: MemberRole[] = ['Reporter', 'Contributor', 'Maintainer']
// Agents can additionally take the Assistant role — a conversational capture helper with no
// lifecycle authority (used by Capture Assist Mode).
const AGENT_ROLES: MemberRole[] = ['Assistant', 'Reporter', 'Contributor', 'Maintainer']

export function SettingsModal({ projectId, onClose }: { projectId: string; onClose: () => void }) {
  return (
    <Modal title="Manage project" onClose={onClose} wide>
      <div className="grid gap-6 md:grid-cols-2">
        <MembersSection projectId={projectId} />
        <SourcesSection projectId={projectId} />
      </div>
    </Modal>
  )
}

function MembersSection({ projectId }: { projectId: string }) {
  const qc = useQueryClient()
  const { data: members } = useMembers(projectId)
  const { data: providers } = useLlmProviders(projectId)
  const { data: actors } = useActors(false)
  const invalidate = () => qc.invalidateQueries({ queryKey: keys.members(projectId) })

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
    <section className="space-y-3">
      <h3 className="text-sm font-semibold text-slate-700 dark:text-slate-200">Members</h3>
      <ul className="space-y-1">
        {(members ?? []).map((m) => (
          <MemberRow key={m.actorId} projectId={projectId} member={m} providers={providers ?? []} />
        ))}
        {members?.length === 0 && <li className="text-sm text-slate-400">No members yet.</li>}
      </ul>

      {/* Add an existing user as a member */}
      <div className="space-y-2 rounded-md border border-slate-200 dark:border-slate-700 p-3">
        <h4 className="text-xs font-semibold uppercase tracking-wide text-slate-400">Add person</h4>
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
      <div className="space-y-2 rounded-md border border-slate-200 dark:border-slate-700 p-3">
        <h4 className="text-xs font-semibold uppercase tracking-wide text-slate-400">Add agent</h4>
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
      <li className="flex items-center gap-2 text-sm">
        <span className="min-w-0 flex-1 truncate text-slate-800 dark:text-slate-100">{member.displayName}</span>
        <Select
          value={member.role}
          className="max-w-36"
          disabled={changeRole.isPending}
          onChange={(e) => changeRole.mutate(e.target.value as MemberRole)}
        >
          {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
        </Select>
        {removeButton}
      </li>
    )
  }

  if (!editing) {
    return (
      <li className="flex items-center gap-2 text-sm">
        <span className="text-slate-800 dark:text-slate-100">{member.displayName}</span>
        <Badge tone="violet">Agent</Badge>
        <Badge tone={member.role === 'Maintainer' ? 'indigo' : 'slate'}>{member.role}</Badge>
        <span className="ml-auto flex items-center gap-1">
          <Button variant="subtle" onClick={() => setEditing(true)}>Edit</Button>
          {removeButton}
        </span>
      </li>
    )
  }

  return (
    <li className="space-y-2 rounded-md border border-slate-200 dark:border-slate-700 p-2">
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
    </li>
  )
}

function SourcesSection({ projectId }: { projectId: string }) {
  const qc = useQueryClient()
  const { data: sources } = useSources(projectId)
  const [type, setType] = useState<ObservationSourceType>('Sdk')
  const [name, setName] = useState('')

  const add = useMutation({
    mutationFn: () => api.createSource(projectId, { type, name, configJson: null }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: keys.sources(projectId) })
      setName('')
    },
  })

  return (
    <section className="space-y-2">
      <h3 className="text-sm font-semibold text-slate-700 dark:text-slate-200">Observation sources</h3>
      <ul className="space-y-1 text-sm">
        {(sources ?? []).map((s) => (
          <li key={s.id} className="text-slate-700 dark:text-slate-200">{s.name} · <span className="text-slate-400">{s.type}</span></li>
        ))}
        {sources?.length === 0 && <li className="text-sm text-slate-400">None configured.</li>}
      </ul>
      <div className="flex items-end gap-2 rounded-md border border-slate-200 dark:border-slate-700 p-3">
        <Field label="Type">
          <Select value={type} onChange={(e) => setType(e.target.value as ObservationSourceType)}>
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
