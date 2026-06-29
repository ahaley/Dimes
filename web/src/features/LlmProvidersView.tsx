import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { useLlmProviders } from '../api/hooks'
import type { LlmProviderConfig, LlmProviderType } from '../api/types'
import { Badge, Button, Card, ErrorText, Field, Select, TextInput } from '../components/ui'

/** App-level management of LLM providers (website-wide + project-scoped). */
export function LlmProvidersView({ projectId }: { projectId: string | undefined }) {
  return (
    <div className="mx-auto max-w-2xl space-y-4">
      <div>
        <h1 className="text-lg font-semibold text-slate-800 dark:text-slate-100">LLM providers</h1>
        <p className="mt-1 text-sm text-slate-500">
          Endpoints used for recommend-only agent commentary. A provider can be website-wide
          (available to every project) or scoped to the current project.
        </p>
      </div>

      {projectId ? (
        <>
          <ProviderList projectId={projectId} />
          <AddProviderForm projectId={projectId} />
        </>
      ) : (
        <Card className="p-6 text-center text-sm text-slate-400">
          Select or create a project to manage providers.
        </Card>
      )}
    </div>
  )
}

function ProviderList({ projectId }: { projectId: string }) {
  const { data: providers } = useLlmProviders(projectId)
  return (
    <Card className="divide-y divide-slate-100 dark:divide-slate-800">
      {(providers ?? []).map((p) => <ProviderRow key={p.id} provider={p} />)}
      {providers?.length === 0 && <p className="p-4 text-sm text-slate-400">No providers configured yet.</p>}
    </Card>
  )
}

function AddProviderForm({ projectId }: { projectId: string }) {
  const qc = useQueryClient()
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
      qc.invalidateQueries({ queryKey: ['providers'] })
      setName('')
    },
  })

  return (
    <Card className="space-y-2 p-4">
      <h2 className="text-sm font-semibold text-slate-700 dark:text-slate-200">Add provider</h2>
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
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
      <label className="flex items-center gap-2 text-xs text-slate-600 dark:text-slate-300">
        <input type="checkbox" checked={websiteWide} onChange={(e) => setWebsiteWide(e.target.checked)} />
        Website-wide (available to all projects)
      </label>
      <ErrorText error={add.error} />
      <Button variant="primary" disabled={!name.trim() || !model.trim() || add.isPending} onClick={() => add.mutate()}>
        {websiteWide ? 'Add website-wide provider' : 'Add provider'}
      </Button>
    </Card>
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
      <div className="flex flex-wrap items-center gap-2 p-3 text-sm text-slate-700 dark:text-slate-200">
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
      </div>
    )
  }

  return (
    <div className="space-y-2 p-3">
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
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
      <label className="flex items-center gap-2 text-xs text-slate-600 dark:text-slate-300">
        <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} /> Enabled
      </label>
      <ErrorText error={save.error} />
      <div className="flex justify-end gap-2">
        <Button variant="subtle" onClick={() => setEditing(false)}>Cancel</Button>
        <Button variant="primary" disabled={!name.trim() || !model.trim() || save.isPending} onClick={() => save.mutate()}>Save</Button>
      </div>
    </div>
  )
}
