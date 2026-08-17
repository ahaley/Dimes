import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { useLlmProviders } from '../api/hooks'
import type { LlmModel, LlmProviderConfig, LlmProviderSettings, LlmProviderType } from '../api/types'
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

// ----- Shared provider form ------------------------------------------------
// The add and edit forms take the same fields, and each provider type adds its own requirements, so the
// shape lives here once. The rules mirror the server's ValidateTypeRequirements; the backend stays the
// authority — this only decides when the save button is worth offering.

/** The editable state of a provider config, flattened for form binding. */
type ProviderDraft = {
  type: LlmProviderType
  name: string
  model: string
  baseUrl: string
  apiKeySecretRef: string
  gcpProject: string
  gcpLocation: string
  useAdc: boolean
}

const TYPE_LABELS: Record<LlmProviderType, string> = {
  Anthropic: 'Anthropic (Claude)',
  Gemini: 'Gemini (Google AI)',
  GeminiVertex: 'Gemini (Vertex AI)',
  OpenAICompatible: 'OpenAI-compatible',
}

/** Per-type placeholders. Kept in one table so the three fields can't drift apart as types are added.
 *
 * These are worth getting right rather than leaving a single generic hint: the base-URL placeholder shows
 * the default host the field would override, and a localhost example under a vendor type would suggest a
 * value the server actually rejects (ProviderUrlValidator pins vendor types to the vendor's domain). The
 * credentials placeholder names the kind of credential — Vertex wants a service-account JSON, not a key. */
const TYPE_PLACEHOLDERS: Record<
  LlmProviderType, { name: string; baseUrl: string; secretRef: string; model: string }
> = {
  Anthropic: {
    name: 'claude',
    baseUrl: 'https://api.anthropic.com',
    secretRef: 'ANTHROPIC_KEY',
    model: 'claude-sonnet-4-6',
  },
  Gemini: {
    name: 'gemini',
    baseUrl: 'https://generativelanguage.googleapis.com',
    secretRef: 'GEMINI_KEY',
    model: 'Discover models, or enter an id',
  },
  GeminiVertex: {
    name: 'vertex',
    baseUrl: 'https://us-central1-aiplatform.googleapis.com',
    secretRef: 'VERTEX_CREDENTIALS',
    // Vertex has no discovery route, so the hint has to stand on its own.
    model: 'Enter a Gemini model id',
  },
  OpenAICompatible: {
    name: 'ollama',
    baseUrl: 'http://localhost:11434/v1',
    secretRef: 'OPENAI_KEY',
    model: 'Discover models, or enter an id',
  },
}

/** The model to prefill per type — empty where there is no id worth asserting.
 *
 * Only Claude gets a real default (the one the spec names). Deliberately no Gemini/OpenAI default: vendors
 * ship and rename models constantly, so a hardcoded id here would be stale within weeks and would only
 * surface as a 400 at first use. Discovery exists precisely so this table doesn't have to guess. */
const DEFAULT_MODELS: Record<LlmProviderType, string> = {
  Anthropic: 'claude-sonnet-4-6',
  Gemini: '',
  GeminiVertex: '',
  OpenAICompatible: '',
}

/** Switch provider type, carrying the model over only if it was still the outgoing type's default.
 *
 * A model id belongs to one vendor, so keeping `claude-sonnet-4-6` after a switch to Vertex just produces
 * a config that saves fine and 400s on first use. But clearing unconditionally would discard an id the
 * operator had typed or discovered, so an edited value is left alone — they can see it no longer fits. */
const withType = (draft: ProviderDraft, type: LlmProviderType): ProviderDraft => ({
  ...draft,
  type,
  model: draft.model.trim() === DEFAULT_MODELS[draft.type] ? DEFAULT_MODELS[type] : draft.model,
})

/** What the base-URL field means per type — it is an override for the vendor types (restricted to that
 * vendor's domain server-side) and the whole point of the OpenAI-compatible one. */
const BASE_URL_HINTS: Record<LlmProviderType, string> = {
  Anthropic: 'Optional. Defaults to api.anthropic.com; an override must stay on anthropic.com.',
  Gemini: 'Optional. Defaults to generativelanguage.googleapis.com; an override must stay on googleapis.com.',
  GeminiVertex: 'Optional. Defaults to the regional Vertex host for the location below.',
  OpenAICompatible:
    'Required for anything but OpenAI itself. Point it at a local runner, an aggregator, or Google’s '
    + 'Gemini compatibility endpoint (https://generativelanguage.googleapis.com/v1beta/openai).',
}

const emptyDraft = (): ProviderDraft => ({
  type: 'Anthropic', name: '', model: DEFAULT_MODELS.Anthropic, baseUrl: '',
  apiKeySecretRef: '', gcpProject: '', gcpLocation: '', useAdc: false,
})

const draftFrom = (provider: LlmProviderConfig): ProviderDraft => ({
  type: provider.type,
  name: provider.name,
  model: provider.model,
  baseUrl: provider.baseUrl ?? '',
  apiKeySecretRef: provider.apiKeySecretRef ?? '',
  gcpProject: provider.settings?.gcpProject ?? '',
  gcpLocation: provider.settings?.gcpLocation ?? '',
  useAdc: provider.settings?.useApplicationDefaultCredentials ?? false,
})

/** Endpoints that always authenticate. OpenAI-compatible stays optional so a keyless local runner works;
 * Vertex is optional only because Application Default Credentials is the alternative. */
const needsKeyRef = (type: LlmProviderType) => type === 'Anthropic' || type === 'Gemini'

/** The reference name to show in the config examples below the field. Before one is typed it falls back to
 * the same placeholder the input displays, so the example reads as a concrete, copyable pair. */
const secretName = (draft: ProviderDraft) =>
  draft.apiKeySecretRef.trim() || TYPE_PLACEHOLDERS[draft.type].secretRef

/** Only Vertex carries settings today; other types send null so the column stays empty. */
const settingsOf = (draft: ProviderDraft): LlmProviderSettings | null =>
  draft.type === 'GeminiVertex'
    ? {
        gcpProject: draft.gcpProject.trim() || null,
        gcpLocation: draft.gcpLocation.trim() || null,
        useApplicationDefaultCredentials: draft.useAdc,
      }
    : null

const writeBody = (draft: ProviderDraft) => ({
  type: draft.type,
  name: draft.name.trim(),
  model: draft.model.trim(),
  baseUrl: draft.baseUrl.trim() || null,
  apiKeySecretRef: draft.apiKeySecretRef.trim() || null,
  settings: settingsOf(draft),
})

function isSaveable(draft: ProviderDraft): boolean {
  if (!draft.name.trim() || !draft.model.trim()) return false
  const hasKeyRef = !!draft.apiKeySecretRef.trim()
  if (needsKeyRef(draft.type) && !hasKeyRef) return false
  if (draft.type === 'GeminiVertex') {
    if (!draft.gcpProject.trim() || !draft.gcpLocation.trim()) return false
    // Exactly one credential source: neither leaves nothing to authenticate with, both leaves the
    // operator unable to tell which one is in use.
    if (draft.useAdc === hasKeyRef) return false
  }
  return true
}

/** Discover the models the endpoint reports, so the model id never has to be guessed or hardcoded.
 * The probe carries the form's current values; the credential is resolved server-side by reference. */
function ModelDiscovery({
  draft, projectId, onPick,
}: {
  draft: ProviderDraft
  projectId: string | null
  onPick: (id: string) => void
}) {
  const [models, setModels] = useState<LlmModel[] | null>(null)
  const probe = { type: draft.type, baseUrl: draft.baseUrl.trim() || null, apiKeySecretRef: draft.apiKeySecretRef.trim() || null, settings: settingsOf(draft) }
  const discover = useMutation({
    mutationFn: () => (projectId ? api.listLlmModels(projectId, probe) : api.listGlobalLlmModels(probe)),
    onSuccess: setModels,
  })

  return (
    <div className="space-y-1">
      <div className="flex items-center gap-2">
        <Button variant="subtle" disabled={discover.isPending} onClick={() => discover.mutate()}>
          {discover.isPending ? 'Discovering…' : 'Discover models'}
        </Button>
        {models !== null && (
          <span className="text-xs text-slate-400">
            {models.length} model{models.length === 1 ? '' : 's'} available
          </span>
        )}
      </div>
      {models !== null && models.length > 0 && (
        <Select value="" onChange={(e) => e.target.value && onPick(e.target.value)}>
          <option value="">Select a discovered model…</option>
          {models.map((m) => (
            <option key={m.id} value={m.id}>{m.displayName ? `${m.id} — ${m.displayName}` : m.id}</option>
          ))}
        </Select>
      )}
      <ErrorText error={discover.error} />
    </div>
  )
}

/** Every field of a provider config. `projectId` is null for a website-wide provider — it selects which
 * model-discovery endpoint to probe, each gated like the matching create. */
function ProviderFields({
  draft, onChange, projectId,
}: {
  draft: ProviderDraft
  onChange: (next: ProviderDraft) => void
  projectId: string | null
}) {
  const set = <K extends keyof ProviderDraft>(key: K, value: ProviderDraft[K]) =>
    onChange({ ...draft, [key]: value })
  const placeholders = TYPE_PLACEHOLDERS[draft.type]

  return (
    <>
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
        <Field label="Type">
          <Select
            value={draft.type}
            onChange={(e) => onChange(withType(draft, e.target.value as LlmProviderType))}
          >
            {(Object.keys(TYPE_LABELS) as LlmProviderType[]).map((t) => (
              <option key={t} value={t}>{TYPE_LABELS[t]}</option>
            ))}
          </Select>
        </Field>
        <Field label="Name">
          <TextInput
            value={draft.name}
            onChange={(e) => set('name', e.target.value)}
            placeholder={placeholders.name}
          />
        </Field>
      </div>

      <Field label="Model">
        <TextInput
          value={draft.model}
          onChange={(e) => set('model', e.target.value)}
          placeholder={placeholders.model}
        />
      </Field>
      <ModelDiscovery draft={draft} projectId={projectId} onPick={(id) => set('model', id)} />

      <Field label="Base URL">
        <TextInput
          value={draft.baseUrl}
          onChange={(e) => set('baseUrl', e.target.value)}
          placeholder={placeholders.baseUrl}
        />
      </Field>
      <p className="text-xs text-slate-400">{BASE_URL_HINTS[draft.type]}</p>

      {draft.type === 'GeminiVertex' && (
        <>
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            <Field label="GCP project">
              <TextInput value={draft.gcpProject} onChange={(e) => set('gcpProject', e.target.value)} placeholder="my-gcp-project" />
            </Field>
            <Field label="Location">
              <TextInput value={draft.gcpLocation} onChange={(e) => set('gcpLocation', e.target.value)} placeholder="us-central1" />
            </Field>
          </div>
          <label className="flex items-center gap-2 text-xs text-slate-600 dark:text-slate-300">
            <input type="checkbox" checked={draft.useAdc} onChange={(e) => set('useAdc', e.target.checked)} />
            Use Application Default Credentials (recommended — no secret is stored)
          </label>
          <p className="text-xs text-slate-400">
            The location picks the regional endpoint, so it is also the data-residency control. With
            Application Default Credentials the host&apos;s own identity is used — Workload Identity on GKE or
            Cloud Run, or the JSON key file that{' '}
            <code className="font-mono">GOOGLE_APPLICATION_CREDENTIALS</code> points at. Otherwise the
            reference below must resolve to a service-account <em>credentials JSON</em> — either the document
            itself or the <em>absolute path</em> of the key file, which Dimes reads.
          </p>
        </>
      )}

      <Field label={draft.type === 'GeminiVertex' ? 'Credentials secret ref' : 'API key secret ref'}>
        <TextInput
          value={draft.apiKeySecretRef}
          onChange={(e) => set('apiKeySecretRef', e.target.value)}
          placeholder={placeholders.secretRef}
          disabled={draft.type === 'GeminiVertex' && draft.useAdc}
        />
      </Field>
      <p className="text-xs text-slate-400">
        A lookup name, not the credential itself. Bind it to a value in configuration
        (<code className="font-mono">Secrets:{secretName(draft)}</code>) or an environment variable of the
        same name — or, for a credential you hold as a file, to its path via{' '}
        <code className="font-mono">SecretFiles:{secretName(draft)}</code> or a{' '}
        <code className="font-mono">{secretName(draft)}_FILE</code> environment variable, and Dimes reads
        the file.{' '}
        {needsKeyRef(draft.type)
          ? 'Required for this provider type.'
          : draft.type === 'GeminiVertex'
            ? 'Required unless Application Default Credentials is enabled. A service-account key is a file, '
              + 'so the simplest binding is the key file’s absolute path as the value — Vertex reads '
              + 'the file either way, so the file forms above work too but are not needed here.'
            : 'Optional for a keyless local endpoint.'}
      </p>
    </>
  )
}

// ----- Views --------------------------------------------------------------

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
  const [draft, setDraft] = useState<ProviderDraft>(emptyDraft)
  const [websiteWide, setWebsiteWide] = useState(false)

  const add = useMutation({
    mutationFn: () => {
      const body = writeBody(draft)
      return websiteWide ? api.createGlobalLlmProvider(body) : api.createLlmProvider(projectId, body)
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['providers'] })
      setDraft({ ...draft, name: '' })
    },
  })

  return (
    <Card className="space-y-2 p-4">
      <h2 className="text-sm font-semibold text-slate-700 dark:text-slate-200">Add provider</h2>
      <ProviderFields draft={draft} onChange={setDraft} projectId={websiteWide ? null : projectId} />
      <label className="flex items-center gap-2 text-xs text-slate-600 dark:text-slate-300">
        <input type="checkbox" checked={websiteWide} onChange={(e) => setWebsiteWide(e.target.checked)} />
        Website-wide (available to all projects)
      </label>
      <ErrorText error={add.error} />
      <Button variant="primary" disabled={!isSaveable(draft) || add.isPending} onClick={() => add.mutate()}>
        {websiteWide ? 'Add website-wide provider' : 'Add provider'}
      </Button>
    </Card>
  )
}

function ProviderRow({ provider }: { provider: LlmProviderConfig }) {
  const qc = useQueryClient()
  const [editing, setEditing] = useState(false)
  const [draft, setDraft] = useState<ProviderDraft>(() => draftFrom(provider))
  const [enabled, setEnabled] = useState(provider.enabled)

  const invalidate = () => qc.invalidateQueries({ queryKey: ['providers'] })
  const save = useMutation({
    mutationFn: () => api.updateLlmProvider(provider.id, { ...writeBody(draft), enabled }),
    onSuccess: () => { invalidate(); setEditing(false) },
  })
  const remove = useMutation({
    mutationFn: () => api.deleteLlmProvider(provider.id),
    onSuccess: invalidate,
  })

  if (!editing) {
    return (
      <div className="flex flex-wrap items-center gap-2 p-3 text-sm text-slate-700 dark:text-slate-200">
        <span>{provider.name} · <span className="text-slate-400">{TYPE_LABELS[provider.type]} / {provider.model}</span></span>
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
      <ProviderFields draft={draft} onChange={setDraft} projectId={provider.projectId ?? null} />
      <label className="flex items-center gap-2 text-xs text-slate-600 dark:text-slate-300">
        <input type="checkbox" checked={enabled} onChange={(e) => setEnabled(e.target.checked)} /> Enabled
      </label>
      <ErrorText error={save.error} />
      <div className="flex justify-end gap-2">
        <Button variant="subtle" onClick={() => setEditing(false)}>Cancel</Button>
        <Button variant="primary" disabled={!isSaveable(draft) || save.isPending} onClick={() => save.mutate()}>
          Save
        </Button>
      </div>
    </div>
  )
}
