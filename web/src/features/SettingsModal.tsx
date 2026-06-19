import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { keys, useLlmProviders, useMembers, useSources } from '../api/hooks'
import type { ActorType, LlmProviderConfig, Member, MemberRole, ObservationSourceType } from '../api/types'
import { Badge, Button, ErrorText, Field, Modal, Select, TextInput } from '../components/ui'

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
  const [displayName, setDisplayName] = useState('')
  const [type, setType] = useState<ActorType>('Human')
  const [role, setRole] = useState<MemberRole>('Contributor')
  const [llmProviderConfigId, setLlm] = useState('')

  const add = useMutation({
    mutationFn: () =>
      api.addMember(projectId, {
        displayName,
        type,
        role,
        email: null,
        llmProviderConfigId: type === 'Agent' && llmProviderConfigId ? llmProviderConfigId : null,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: keys.members(projectId) })
      setDisplayName('')
    },
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

      <div className="space-y-2 rounded-md border border-slate-200 dark:border-slate-700 p-3">
        <Field label="Display name">
          <TextInput value={displayName} onChange={(e) => setDisplayName(e.target.value)} placeholder="Jordan" />
        </Field>
        <div className="grid grid-cols-2 gap-2">
          <Field label="Type">
            <Select value={type} onChange={(e) => setType(e.target.value as ActorType)}>
              <option value="Human">Human</option>
              <option value="Agent">Agent</option>
            </Select>
          </Field>
          <Field label="Role">
            <Select value={role} onChange={(e) => setRole(e.target.value as MemberRole)}>
              <option value="Reporter">Reporter</option>
              <option value="Contributor">Contributor</option>
              <option value="Maintainer">Maintainer</option>
            </Select>
          </Field>
        </div>
        {type === 'Agent' && (
          <Field label="LLM provider (for commentary)">
            <Select value={llmProviderConfigId} onChange={(e) => setLlm(e.target.value)}>
              <option value="">none</option>
              {(providers ?? []).map((p) => (
                <option key={p.id} value={p.id}>{p.name} ({p.model})</option>
              ))}
            </Select>
          </Field>
        )}
        <ErrorText error={add.error} />
        <Button variant="primary" disabled={!displayName.trim() || add.isPending} onClick={() => add.mutate()}>
          Add member
        </Button>
      </div>
    </section>
  )
}

function MemberRow({
  projectId, member, providers,
}: { projectId: string; member: Member; providers: LlmProviderConfig[] }) {
  const qc = useQueryClient()
  const [editing, setEditing] = useState(false)
  const [displayName, setDisplayName] = useState(member.displayName)
  const [role, setRole] = useState<MemberRole>(member.role)
  const [llm, setLlm] = useState(member.llmProviderConfigId ?? '')

  const invalidate = () => qc.invalidateQueries({ queryKey: keys.members(projectId) })
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

  if (!editing) {
    return (
      <li className="flex items-center gap-2 text-sm">
        <span className="text-slate-800 dark:text-slate-100">{member.displayName}</span>
        <Badge tone={member.type === 'Agent' ? 'violet' : 'slate'}>{member.type}</Badge>
        <Badge tone={member.role === 'Maintainer' ? 'indigo' : 'slate'}>{member.role}</Badge>
        <span className="ml-auto flex items-center gap-1">
          <Button variant="subtle" onClick={() => setEditing(true)}>Edit</Button>
          <Button
            variant="subtle"
            disabled={remove.isPending}
            onClick={() => { if (window.confirm(`Remove ${member.displayName} from this project?`)) remove.mutate() }}
          >
            Remove
          </Button>
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
          <option value="Reporter">Reporter</option>
          <option value="Contributor">Contributor</option>
          <option value="Maintainer">Maintainer</option>
        </Select>
      </Field>
      {member.type === 'Agent' && (
        <Field label="LLM provider (for commentary)">
          <Select value={llm} onChange={(e) => setLlm(e.target.value)}>
            <option value="">none</option>
            {providers.map((p) => <option key={p.id} value={p.id}>{p.name} ({p.model})</option>)}
          </Select>
        </Field>
      )}
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
