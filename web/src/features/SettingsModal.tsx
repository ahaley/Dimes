import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { keys, useLlmProviders, useMembers, useSources } from '../api/hooks'
import type { ActorType, LlmProviderConfig, LlmProviderType, Member, MemberRole, ObservationSourceType } from '../api/types'
import { Badge, Button, ErrorText, Field, Modal, Select, TextInput } from '../components/ui'

export function SettingsModal({ projectId, onClose }: { projectId: string; onClose: () => void }) {
  return (
    <Modal title="Manage project" onClose={onClose} wide>
      <div className="grid gap-6 md:grid-cols-2">
        <MembersSection projectId={projectId} />
        <div className="space-y-6">
          <LlmProvidersSection projectId={projectId} />
          <SourcesSection projectId={projectId} />
        </div>
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
      <h3 className="text-sm font-semibold text-slate-700">Members</h3>
      <ul className="space-y-1">
        {(members ?? []).map((m) => (
          <MemberRow key={m.actorId} projectId={projectId} member={m} providers={providers ?? []} />
        ))}
        {members?.length === 0 && <li className="text-sm text-slate-400">No members yet.</li>}
      </ul>

      <div className="space-y-2 rounded-md border border-slate-200 p-3">
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
        <span className="text-slate-800">{member.displayName}</span>
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
    <li className="space-y-2 rounded-md border border-slate-200 p-2">
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

function LlmProvidersSection({ projectId }: { projectId: string }) {
  const qc = useQueryClient()
  const { data: providers } = useLlmProviders(projectId)
  const [type, setType] = useState<LlmProviderType>('Anthropic')
  const [name, setName] = useState('')
  const [model, setModel] = useState('claude-sonnet-4-6')
  const [baseUrl, setBaseUrl] = useState('')
  const [apiKeySecretRef, setSecret] = useState('')
  const [websiteWide, setWebsiteWide] = useState(false)

  const add = useMutation({
    mutationFn: () => {
      const body = { type, name, model, baseUrl: baseUrl || null, apiKeySecretRef: apiKeySecretRef || null }
      return websiteWide ? api.createGlobalLlmProvider(body) : api.createLlmProvider(projectId, body)
    },
    onSuccess: () => {
      // A global provider affects every project's available list, so invalidate broadly.
      qc.invalidateQueries({ queryKey: ['providers'] })
      setName('')
    },
  })

  return (
    <section className="space-y-2">
      <h3 className="text-sm font-semibold text-slate-700">LLM providers</h3>
      <ul className="space-y-1 text-sm">
        {(providers ?? []).map((p) => (
          <ProviderRow key={p.id} provider={p} />
        ))}
        {providers?.length === 0 && <li className="text-sm text-slate-400">None configured.</li>}
      </ul>
      <div className="space-y-2 rounded-md border border-slate-200 p-3">
        <div className="grid grid-cols-2 gap-2">
          <Field label="Type">
            <Select value={type} onChange={(e) => setType(e.target.value as LlmProviderType)}>
              <option value="Anthropic">Anthropic</option>
              <option value="OpenAICompatible">OpenAI-compatible</option>
            </Select>
          </Field>
          <Field label="Name">
            <TextInput value={name} onChange={(e) => setName(e.target.value)} placeholder="claude" />
          </Field>
        </div>
        <Field label="Model"><TextInput value={model} onChange={(e) => setModel(e.target.value)} /></Field>
        <Field label="Base URL (OpenAI-compatible / local)">
          <TextInput value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} placeholder="http://localhost:11434/v1" />
        </Field>
        <Field label="API key secret ref">
          <TextInput value={apiKeySecretRef} onChange={(e) => setSecret(e.target.value)} placeholder="ANTHROPIC_KEY" />
        </Field>
        <label className="flex items-center gap-2 text-xs text-slate-600">
          <input type="checkbox" checked={websiteWide} onChange={(e) => setWebsiteWide(e.target.checked)} />
          Website-wide (available to all projects)
        </label>
        <ErrorText error={add.error} />
        <Button variant="primary" disabled={!name.trim() || !model.trim() || add.isPending} onClick={() => add.mutate()}>
          {websiteWide ? 'Add website-wide provider' : 'Add provider'}
        </Button>
      </div>
    </section>
  )
}

function ProviderRow({ provider }: { provider: LlmProviderConfig }) {
  const qc = useQueryClient()
  const [editing, setEditing] = useState(false)
  const [type, setType] = useState<LlmProviderType>(provider.type)
  const [name, setName] = useState(provider.name)
  const [model, setModel] = useState(provider.model)
  const [baseUrl, setBaseUrl] = useState(provider.baseUrl ?? '')
  const [apiKeySecretRef, setSecret] = useState(provider.apiKeySecretRef ?? '')
  const [enabled, setEnabled] = useState(provider.enabled)

  const invalidate = () => qc.invalidateQueries({ queryKey: ['providers'] })
  const save = useMutation({
    mutationFn: () =>
      api.updateLlmProvider(provider.id, {
        type, name, model, baseUrl: baseUrl || null, apiKeySecretRef: apiKeySecretRef || null, enabled,
      }),
    onSuccess: () => { invalidate(); setEditing(false) },
  })
  const remove = useMutation({
    mutationFn: () => api.deleteLlmProvider(provider.id),
    onSuccess: invalidate,
  })

  if (!editing) {
    return (
      <li className="flex items-center gap-2 text-slate-700">
        <span>{provider.name} · <span className="text-slate-400">{provider.type} / {provider.model}</span></span>
        {provider.projectId === null && <Badge tone="indigo">website-wide</Badge>}
        {!provider.enabled && <Badge tone="red">disabled</Badge>}
        <span className="ml-auto flex items-center gap-1">
          <Button variant="subtle" onClick={() => setEditing(true)}>Edit</Button>
          <Button
            variant="subtle"
            disabled={remove.isPending}
            onClick={() => { if (window.confirm(`Delete provider "${provider.name}"?`)) remove.mutate() }}
          >
            Delete
          </Button>
        </span>
        <ErrorText error={remove.error} />
      </li>
    )
  }

  return (
    <li className="space-y-2 rounded-md border border-slate-200 p-2">
      <div className="grid grid-cols-2 gap-2">
        <Field label="Type">
          <Select value={type} onChange={(e) => setType(e.target.value as LlmProviderType)}>
            <option value="Anthropic">Anthropic</option>
            <option value="OpenAICompatible">OpenAI-compatible</option>
          </Select>
        </Field>
        <Field label="Name"><TextInput value={name} onChange={(e) => setName(e.target.value)} /></Field>
      </div>
      <Field label="Model"><TextInput value={model} onChange={(e) => setModel(e.target.value)} /></Field>
      <Field label="Base URL"><TextInput value={baseUrl} onChange={(e) => setBaseUrl(e.target.value)} placeholder="http://localhost:11434/v1" /></Field>
      <Field label="API key secret ref"><TextInput value={apiKeySecretRef} onChange={(e) => setSecret(e.target.value)} placeholder="ANTHROPIC_KEY" /></Field>
      <label className="flex items-center gap-2 text-xs text-slate-600">
        <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} /> Enabled
      </label>
      <ErrorText error={save.error} />
      <div className="flex justify-end gap-2">
        <Button variant="subtle" onClick={() => setEditing(false)}>Cancel</Button>
        <Button variant="primary" disabled={!name.trim() || !model.trim() || save.isPending} onClick={() => save.mutate()}>Save</Button>
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
      <h3 className="text-sm font-semibold text-slate-700">Observation sources</h3>
      <ul className="space-y-1 text-sm">
        {(sources ?? []).map((s) => (
          <li key={s.id} className="text-slate-700">{s.name} · <span className="text-slate-400">{s.type}</span></li>
        ))}
        {sources?.length === 0 && <li className="text-sm text-slate-400">None configured.</li>}
      </ul>
      <div className="flex items-end gap-2 rounded-md border border-slate-200 p-3">
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
