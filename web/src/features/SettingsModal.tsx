import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { keys, useLlmProviders, useMembers, useSources } from '../api/hooks'
import type { ActorType, LlmProviderType, MemberRole, ObservationSourceType } from '../api/types'
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
          <li key={m.actorId} className="flex items-center gap-2 text-sm">
            <span className="text-slate-800">{m.displayName}</span>
            <Badge tone={m.type === 'Agent' ? 'violet' : 'slate'}>{m.type}</Badge>
            <Badge tone={m.role === 'Maintainer' ? 'indigo' : 'slate'}>{m.role}</Badge>
          </li>
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

function LlmProvidersSection({ projectId }: { projectId: string }) {
  const qc = useQueryClient()
  const { data: providers } = useLlmProviders(projectId)
  const [type, setType] = useState<LlmProviderType>('Anthropic')
  const [name, setName] = useState('')
  const [model, setModel] = useState('claude-sonnet-4-6')
  const [baseUrl, setBaseUrl] = useState('')
  const [apiKeySecretRef, setSecret] = useState('')

  const add = useMutation({
    mutationFn: () =>
      api.createLlmProvider(projectId, {
        type,
        name,
        model,
        baseUrl: baseUrl || null,
        apiKeySecretRef: apiKeySecretRef || null,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: keys.providers(projectId) })
      setName('')
    },
  })

  return (
    <section className="space-y-2">
      <h3 className="text-sm font-semibold text-slate-700">LLM providers</h3>
      <ul className="space-y-1 text-sm">
        {(providers ?? []).map((p) => (
          <li key={p.id} className="text-slate-700">{p.name} · <span className="text-slate-400">{p.type} / {p.model}</span></li>
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
        <ErrorText error={add.error} />
        <Button variant="primary" disabled={!name.trim() || !model.trim() || add.isPending} onClick={() => add.mutate()}>
          Add provider
        </Button>
      </div>
    </section>
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
