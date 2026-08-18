import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { useGlobalLlmProviders, useLlmProviders, useProjects } from '../api/hooks'
import type { LlmModel, LlmProviderConfig, LlmProviderSettings, LlmProviderType, LlmReasoning, Project } from '../api/types'
import { SecretRefField } from '../components/SecretRefField'
import { Badge, Button, Card, ErrorText, Field, Select, TextInput } from '../components/ui'

/** The scope a provider belongs to. '' is website-wide; anything else is a project id. */
type Scope = string
const WEBSITE_WIDE: Scope = ''

/** How a project reads in the scope picker and section headings — matches the header-title convention
 *  in App.tsx, and marks archived projects so an existing provider on one is still reachable. */
function projectLabel(project: Project): string {
  const name = project.key ? `${project.key} · ${project.name}` : project.name
  return project.isArchived ? `${name} (archived)` : name
}

/** App-level management of LLM providers (website-wide + project-scoped).
 *
 * Scope is an explicit choice here rather than inherited from whichever project the admin last visited.
 * This view sits under the sidebar's site-admin Settings group, not under a project, so an inherited
 * project context was invisible — the project was never named, and a direct load of /providers (where no
 * route match supplies one) fell through to an empty state. It now always opens on website-wide. */
export function LlmProvidersView() {
  const [scope, setScope] = useState<Scope>(WEBSITE_WIDE)
  // Include archived so a provider scoped to one doesn't become unmanageable.
  const { data: projects } = useProjects(true, true)
  const selected = (projects ?? []).find((p) => p.id === scope)

  return (
    <div className="mx-auto max-w-2xl space-y-4">
      <div>
        <h1 className="text-lg font-semibold text-slate-800 dark:text-slate-100">LLM providers</h1>
        <p className="mt-1 text-sm text-slate-500">
          Endpoints used for recommend-only agent commentary. A provider is either website-wide — available
          to every project — or scoped to one project. Choose which set you&apos;re managing.
        </p>
      </div>

      <Card className="p-4">
        <Field label="Scope">
          <Select value={scope} onChange={(e) => setScope(e.target.value)}>
            <option value={WEBSITE_WIDE}>Website-wide</option>
            {(projects ?? []).map((p) => (
              <option key={p.id} value={p.id}>{projectLabel(p)}</option>
            ))}
          </Select>
        </Field>
      </Card>

      {scope === WEBSITE_WIDE
        ? <WebsiteWideScope />
        : <ProjectScope projectId={scope} heading={selected ? projectLabel(selected) : 'This project'} />}
    </div>
  )
}

/** Website-wide providers: the whole list is in scope, so there is nothing inherited to show. */
function WebsiteWideScope() {
  const { data: providers } = useGlobalLlmProviders()
  return (
    <>
      <ProviderList
        heading="Website-wide · available to every project"
        providers={providers}
        empty="No website-wide providers yet."
      />
      <AddProviderForm scope={WEBSITE_WIDE} scopeLabel="website-wide" />
    </>
  )
}

/** One project's own providers, plus the website-wide ones it inherits shown read-only — so the list
 *  answers "what can this project's agents actually use?" without implying they're managed here. */
function ProjectScope({ projectId, heading }: { projectId: string; heading: string }) {
  const { data: providers } = useLlmProviders(projectId)
  const own = (providers ?? []).filter((p) => p.projectId !== null)
  const inherited = (providers ?? []).filter((p) => p.projectId === null)

  return (
    <>
      <ProviderList
        heading={heading}
        providers={providers === undefined ? undefined : own}
        empty="No providers scoped to this project yet."
      />
      {inherited.length > 0 && (
        <Card className="p-4">
          <h2 className="text-sm font-semibold text-slate-700 dark:text-slate-200">
            Also available here, from website-wide
          </h2>
          <p className="mt-1 text-xs text-slate-400">
            Manage these under the Website-wide scope.
          </p>
          <ul className="mt-2 space-y-1 text-sm text-slate-600 dark:text-slate-300">
            {inherited.map((p) => (
              <li key={p.id}>
                {p.name} · <span className="text-slate-400">{TYPE_LABELS[p.type]} / {p.model}</span>
                {!p.enabled && <> <Badge tone="red">disabled</Badge></>}
              </li>
            ))}
          </ul>
        </Card>
      )}
      <AddProviderForm scope={projectId} scopeLabel={heading} />
    </>
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
  /** '' = don't override; Dimes asks each call for what it wants. Anthropic only. */
  reasoning: LlmReasoning | ''
  /** '' = don't override. Kept as text so a half-typed number isn't coerced to 0 mid-keystroke. */
  maxTokens: string
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
    model: 'Discover models, or enter an id',
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
 * operator had typed or discovered, so an edited value is left alone — they can see it no longer fits.
 *
 * Switching *into* Vertex also restores its one-credential-source invariant. ADC and a reference are
 * mutually exclusive, and both can be set on arrival — tick ADC under Vertex, switch to Anthropic, type a
 * key reference, switch back. Left alone that reproduces the dead end `setUseAdc` exists to prevent: an
 * unsaveable draft whose reference field is disabled and so can't be emptied. ADC yields rather than the
 * reference, because the reference is the value the operator typed most recently and can see.
 *
 * Switching *away* from Anthropic clears any reasoning override for the same reason: the server refuses
 * one on a type whose adapter ignores reasoning, and the field that holds it is hidden outside Anthropic —
 * so leaving it set would make the draft unsaveable with nothing on screen to explain why. The token
 * budget override survives the switch; it means the same thing on every type. */
const withType = (draft: ProviderDraft, type: LlmProviderType): ProviderDraft => ({
  ...draft,
  type,
  model: draft.model.trim() === DEFAULT_MODELS[draft.type] ? DEFAULT_MODELS[type] : draft.model,
  useAdc: type === 'GeminiVertex' && draft.apiKeySecretRef.trim() ? false : draft.useAdc,
  reasoning: type === 'Anthropic' ? draft.reasoning : '',
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
  reasoning: '', maxTokens: '',
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
  reasoning: provider.settings?.reasoning ?? '',
  maxTokens: provider.settings?.maxTokens?.toString() ?? '',
})

/** Endpoints that always authenticate. OpenAI-compatible stays optional so a keyless local runner works;
 * Vertex is optional only because Application Default Credentials is the alternative. */
const needsKeyRef = (type: LlmProviderType) => type === 'Anthropic' || type === 'Gemini'

/** Whether the reference is required, in words. Derived from needsKeyRef rather than spelled out per type,
 * so the sentence can't drift from the rule that actually gates saving. Vertex says only that ADC is the
 * alternative — what its value may contain is already stated by the ADC paragraph directly above. */
const secretRefRequirement = (draft: ProviderDraft) => {
  if (needsKeyRef(draft.type)) return 'Required for this provider type.'
  if (draft.type === 'GeminiVertex') return 'Required unless Application Default Credentials is on.'
  return 'Optional for a keyless local endpoint.'
}

/** A positive integer, or null for "not overridden" — anything unparseable is null so a half-typed value
 *  is simply absent rather than sent as 0 or NaN. `isSaveable` is what refuses it. */
const overrideNumber = (value: string): number | null => {
  const trimmed = value.trim()
  if (!trimmed) return null
  const parsed = Number(trimmed)
  return Number.isInteger(parsed) && parsed > 0 ? parsed : null
}

/** The settings block, or null when nothing in it is set — Vertex always carries one (project/region are
 *  required there), and any type carries one once a call override is set. Null keeps the JSON column empty
 *  for the common case rather than storing a blob of nulls. */
const settingsOf = (draft: ProviderDraft): LlmProviderSettings | null => {
  const isVertex = draft.type === 'GeminiVertex'
  // Only Anthropic acts on a reasoning override, and the server refuses one elsewhere; `withType` already
  // clears it on a switch, so this is belt-and-braces for a draft built any other way.
  const reasoning = draft.type === 'Anthropic' && draft.reasoning ? draft.reasoning : null
  const maxTokens = overrideNumber(draft.maxTokens)
  if (!isVertex && reasoning === null && maxTokens === null) return null
  return {
    gcpProject: isVertex ? draft.gcpProject.trim() || null : null,
    gcpLocation: isVertex ? draft.gcpLocation.trim() || null : null,
    useApplicationDefaultCredentials: isVertex && draft.useAdc,
    reasoning,
    maxTokens,
  }
}

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
  // Typed but not a positive integer: refuse rather than silently dropping it, or the operator would save
  // and never learn their budget override wasn't stored.
  if (draft.maxTokens.trim() && overrideNumber(draft.maxTokens) === null) return false
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
      {/* Vertex lists Google's published catalog rather than what this project may call, so a listed id
          can still be refused. Said up front, because the alternative is a confusing failure later. */}
      {draft.type === 'GeminiVertex' && models !== null && models.length > 0 && (
        <p className="text-xs text-slate-400">
          These are the Gemini models Google publishes. Availability still depends on your project and
          region, so confirm the one you pick is enabled for {draft.gcpLocation.trim() || 'your region'}.
        </p>
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

  // Turning ADC on clears the credentials reference, because the two are mutually exclusive both here
  // (isSaveable) and server-side (ValidateTypeRequirements). Without the clear, ticking ADC on a provider
  // that already had a reference was a dead end: the save became invalid while the field it was invalid
  // for went disabled, so the value could not be removed and nothing said why. Clearing also keeps the
  // greyed-out field honest — it shows what will actually be saved, rather than a value that is ignored.
  const setUseAdc = (useAdc: boolean) =>
    onChange({ ...draft, useAdc, apiKeySecretRef: useAdc ? '' : draft.apiKeySecretRef })

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
            <input type="checkbox" checked={draft.useAdc} onChange={(e) => setUseAdc(e.target.checked)} />
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

      <SecretRefField
        label={draft.type === 'GeminiVertex' ? 'Credentials secret ref' : 'API key secret ref'}
        namespace="Llm"
        value={draft.apiKeySecretRef}
        onChange={(next) => set('apiKeySecretRef', next)}
        placeholder={placeholders.secretRef}
        disabled={draft.type === 'GeminiVertex' && draft.useAdc}
        requirement={secretRefRequirement(draft)}
      />

      <CallOverrides draft={draft} set={set} />
    </>
  )
}

/** How each reasoning override reads. '' is first and is what every provider should stay on: Dimes asks
 *  each call for what that call needs, which is currently "off" everywhere. */
const REASONING_LABELS: Record<LlmReasoning | '', string> = {
  '': 'Follow the request (recommended)',
  Disabled: 'Always off',
  Adaptive: 'Adaptive — the model decides',
  VendorDefault: 'Model default — send no reasoning setting',
}

/** Per-endpoint overrides of what a call asks for.
 *
 * These exist for one concrete failure: a model whose reasoning cannot be turned off. Dimes asks every
 * call for reasoning-off, which is right for short recommend-only output — but Anthropic's Fable/Mythos
 * family reasons unconditionally and rejects an explicit reasoning setting outright, so such a config
 * can't complete a call until it stops sending one. Making that legal isn't enough on its own: reasoning
 * comes out of the same budget as the answer, so the same config also needs a bigger one. The two knobs
 * sit together because they are one decision.
 *
 * The reasoning control is Anthropic-only because it is the only adapter that sends a reasoning parameter
 * at all — the server refuses the setting elsewhere rather than store something inert. The budget applies
 * to every type. */
function CallOverrides({
  draft, set,
}: {
  draft: ProviderDraft
  set: <K extends keyof ProviderDraft>(key: K, value: ProviderDraft[K]) => void
}) {
  const badMaxTokens = !!draft.maxTokens.trim() && overrideNumber(draft.maxTokens) === null

  return (
    <div className="space-y-1 border-t border-slate-100 pt-3 dark:border-slate-800">
      <div className="text-xs font-semibold uppercase tracking-wide text-slate-400">Call settings</div>
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
        {draft.type === 'Anthropic' && (
          <Field label="Reasoning">
            <Select
              value={draft.reasoning}
              onChange={(e) => set('reasoning', e.target.value as LlmReasoning | '')}
            >
              {(Object.keys(REASONING_LABELS) as (LlmReasoning | '')[]).map((r) => (
                <option key={r} value={r}>{REASONING_LABELS[r]}</option>
              ))}
            </Select>
          </Field>
        )}
        <Field label="Max output tokens">
          <TextInput
            inputMode="numeric"
            value={draft.maxTokens}
            onChange={(e) => set('maxTokens', e.target.value)}
            placeholder="Dimes decides (1024–2048)"
          />
        </Field>
      </div>
      {badMaxTokens && <ErrorText error="Enter a whole number greater than zero, or leave it empty." />}
      <p className="text-xs text-slate-400">
        Leave both alone unless a model needs it. Set reasoning to{' '}
        <span className="font-semibold">{REASONING_LABELS.VendorDefault}</span> for a model that always
        reasons and rejects being told not to — and raise the budget with it, because reasoning is drawn
        from the same allowance as the answer, so the default would be spent before any text is written.
      </p>
    </div>
  )
}

// ----- Views --------------------------------------------------------------

/** The providers owned by the selected scope. The heading names that scope, so no row is left implicitly
 *  belonging to something the reader can't see. */
function ProviderList({
  heading, providers, empty,
}: {
  heading: string
  providers: LlmProviderConfig[] | undefined
  empty: string
}) {
  return (
    <Card>
      <h2 className="border-b border-slate-100 px-4 py-2 text-sm font-semibold text-slate-700 dark:border-slate-800 dark:text-slate-200">
        {heading}
      </h2>
      <div className="divide-y divide-slate-100 dark:divide-slate-800">
        {(providers ?? []).map((p) => <ProviderRow key={p.id} provider={p} />)}
        {providers?.length === 0 && <p className="p-4 text-sm text-slate-400">{empty}</p>}
      </div>
    </Card>
  )
}

/** Adds into the selected scope. There is no website-wide checkbox any more — the scope picker above owns
 *  that choice, and having it in two places was the thing that made scope hard to read. */
function AddProviderForm({ scope, scopeLabel }: { scope: Scope; scopeLabel: string }) {
  const qc = useQueryClient()
  const [draft, setDraft] = useState<ProviderDraft>(emptyDraft)

  const add = useMutation({
    mutationFn: () => {
      const body = writeBody(draft)
      return scope === WEBSITE_WIDE ? api.createGlobalLlmProvider(body) : api.createLlmProvider(scope, body)
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['providers'] })
      setDraft({ ...draft, name: '' })
    },
  })

  return (
    <Card className="space-y-2 p-4">
      <h2 className="text-sm font-semibold text-slate-700 dark:text-slate-200">
        Add provider to {scopeLabel}
      </h2>
      <ProviderFields
        draft={draft}
        onChange={setDraft}
        projectId={scope === WEBSITE_WIDE ? null : scope}
      />
      <ErrorText error={add.error} />
      <Button variant="primary" disabled={!isSaveable(draft) || add.isPending} onClick={() => add.mutate()}>
        Add provider
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
        {/* Badge both scopes, not just website-wide: an unbadged row previously meant "belongs to some
            project", which is exactly the ambiguity being fixed. */}
        <Badge tone={provider.projectId === null ? 'indigo' : 'slate'}>
          {provider.projectId === null ? 'website-wide' : 'project'}
        </Badge>
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
      <MoveScope provider={provider} />
    </div>
  )
}

/** Moving a provider between scopes is its own endpoint and so its own control — saving the fields above
 *  does not change scope, and a move can be refused (it would strand an agent whose project loses access),
 *  which needs somewhere of its own to report. */
function MoveScope({ provider }: { provider: LlmProviderConfig }) {
  const qc = useQueryClient()
  const { data: projects } = useProjects(true, true)
  const current: Scope = provider.projectId ?? WEBSITE_WIDE
  const [target, setTarget] = useState<Scope>(current)

  const move = useMutation({
    mutationFn: () => api.moveLlmProviderScope(provider.id, target === WEBSITE_WIDE ? null : target),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['providers'] }),
  })

  return (
    <div className="space-y-1 border-t border-slate-100 pt-2 dark:border-slate-800">
      <Field label="Scope">
        <Select value={target} onChange={(e) => setTarget(e.target.value)}>
          <option value={WEBSITE_WIDE}>Website-wide</option>
          {(projects ?? []).map((p) => (
            <option key={p.id} value={p.id}>{projectLabel(p)}</option>
          ))}
        </Select>
      </Field>
      <p className="text-xs text-slate-400">
        Moving to a project is refused while an agent outside it still uses this provider — reassign those
        agents first, so none is left pointing at a provider its project can&apos;t see.
      </p>
      <ErrorText error={move.error} />
      <div className="flex justify-end">
        <Button
          variant="subtle"
          disabled={target === current || move.isPending}
          onClick={() => move.mutate()}
        >
          {move.isPending ? 'Moving…' : 'Move scope'}
        </Button>
      </div>
    </div>
  )
}
