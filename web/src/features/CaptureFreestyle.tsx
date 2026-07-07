import { useEffect, useMemo, useRef, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { useProjectInvalidator } from '../api/hooks'
import type { ChangeKind, Member, Priority } from '../api/types'
import { Badge, Button, cx, ErrorText, Field, Select, TextInput, Textarea } from '../components/ui'
import { useToast } from '../components/Toast'
import { kindTone } from '../lifecycle'

const KINDS: ChangeKind[] = ['Feature', 'Problem', 'ObservationDriven', 'Epic', 'Chore']
const PRIORITIES: Priority[] = ['None', 'Low', 'Medium', 'High', 'Critical']
const DEBOUNCE_MS = 1200
const MIN_MARKDOWN = 8 // skip generation until the brief has some substance

// The freestyle brief lives only in local component state and is never persisted server-side until the
// user confirms the batch. Stash it in localStorage (per project) so navigating away — a route change,
// a mode switch, or a full reload — and coming back restores the in-progress brief instead of losing it.
const freestyleDraftKey = (projectId: string) => `dimes.captureAssist.freestyle.${projectId}`

// Client-only editable proposal. `id` is a stable React key; it is never sent to the create endpoint.
type Proposal = { id: string; title: string; description: string; kind: ChangeKind; priority: Priority }

/**
 * Capture Assist — Freestyle Mode. The user writes a freeform markdown brief on the left; an Agent's
 * LLM decomposes it into a list of proposed change orders on the right, which the user can edit, add to,
 * or delete. Generation runs on demand via the Generate button, or — when the user opts in with the
 * Auto toggle — automatically a short while after they stop typing. Confirming creates them all in the
 * Captured state in one batch. Recommend-only: nothing is created until the user confirms.
 */
export function CaptureFreestyle({ projectId, agents, zen = false }: { projectId: string; agents: Member[]; zen?: boolean }) {
  const navigate = useNavigate()
  const toast = useToast()
  const invalidate = useProjectInvalidator(projectId)

  const defaultAgentId =
    (agents.find((a) => a.role === 'Assistant') ?? agents[0])?.actorId ?? ''
  const [agentId, setAgentId] = useState('')
  const activeAgentId = agentId || defaultAgentId

  const draftKey = freestyleDraftKey(projectId)
  const [markdown, setMarkdown] = useState(() => {
    try { return localStorage.getItem(draftKey) ?? '' } catch { return '' }
  })
  const [proposals, setProposals] = useState<Proposal[]>([])
  const [auto, setAuto] = useState(false)
  // Once the user hand-edits the proposals, freeze auto-generation so it never clobbers their work —
  // they must press Regenerate to refresh from the brief.
  const [dirty, setDirty] = useState(false)
  // Markdown text of the last dispatched generation, so we never re-request unchanged input.
  const lastGenerated = useRef('')

  const generate = useMutation({
    mutationFn: (md: string) => api.generateProposals(projectId, { agentActorId: activeAgentId, markdown: md }),
    onSuccess: (res) => {
      setProposals(res.proposals.map((p) => ({
        id: crypto.randomUUID(),
        title: p.title,
        description: p.description ?? '',
        kind: p.kind,
        priority: p.priority,
      })))
      setDirty(false)
    },
  })

  const runGenerate = (md: string) => {
    if (!activeAgentId || generate.isPending) return
    if (md.trim().length < MIN_MARKDOWN) return
    if (md === lastGenerated.current) return
    lastGenerated.current = md
    generate.mutate(md)
  }

  // Auto mode: debounce a generation a short while after the last keystroke. Disabled once the user has
  // hand-edited proposals (dirty), and a no-op while a request is already in flight or input is unchanged.
  useEffect(() => {
    if (!auto || dirty) return
    const t = setTimeout(() => runGenerate(markdown), DEBOUNCE_MS)
    return () => clearTimeout(t)
    // runGenerate reads refs/mutation state; markdown/auto/dirty are the meaningful triggers.
  }, [markdown, auto, dirty]) // eslint-disable-line react-hooks/exhaustive-deps

  // Persist the brief so leaving and returning (or reloading) restores it. Empty briefs clear the key.
  useEffect(() => {
    try {
      if (markdown) localStorage.setItem(draftKey, markdown)
      else localStorage.removeItem(draftKey)
    } catch { /* storage unavailable — non-fatal */ }
  }, [markdown, draftKey])

  // Belt-and-suspenders for the one exit we can't restore from cleanly: a hard tab close/reload while
  // the brief has unsaved substance prompts the browser's leave-confirmation.
  useEffect(() => {
    if (!markdown.trim()) return
    const onBeforeUnload = (e: BeforeUnloadEvent) => { e.preventDefault(); e.returnValue = '' }
    window.addEventListener('beforeunload', onBeforeUnload)
    return () => window.removeEventListener('beforeunload', onBeforeUnload)
  }, [markdown])

  // Which proposal is expanded into its full edit form; null means every card is compact. Editing one at
  // a time keeps the list scannable — the compact default is the read-only presentation.
  const [editingId, setEditingId] = useState<string | null>(null)

  const update = (id: string, patch: Partial<Proposal>) => {
    setProposals((ps) => ps.map((p) => (p.id === id ? { ...p, ...patch } : p)))
    setDirty(true)
  }
  const addProposal = () => {
    const id = crypto.randomUUID()
    setProposals((ps) => [...ps, { id, title: '', description: '', kind: 'Feature', priority: 'None' }])
    // A brand-new card has nothing to read, so open it straight into edit mode.
    setEditingId(id)
    setDirty(true)
  }
  const removeProposal = (id: string) => {
    setProposals((ps) => ps.filter((p) => p.id !== id))
    setEditingId((cur) => (cur === id ? null : cur))
    setDirty(true)
  }

  const validCount = useMemo(() => proposals.filter((p) => p.title.trim()).length, [proposals])

  const confirm = useMutation({
    mutationFn: () =>
      api.createChangesBatch(projectId, {
        changes: proposals
          .filter((p) => p.title.trim())
          .map((p) => ({
            title: p.title.trim(),
            description: p.description.trim() || null,
            kind: p.kind,
            priority: p.priority,
          })),
      }),
    onSuccess: (created) => {
      // The brief has done its job — drop the persisted draft so it doesn't resurrect on return.
      try { localStorage.removeItem(draftKey) } catch { /* non-fatal */ }
      invalidate()
      toast.success(`${created.length} change request${created.length === 1 ? '' : 's'} captured`)
      navigate(`/projects/${projectId}`)
    },
    onError: (e) => toast.error(e instanceof Error ? e.message : 'Could not create the changes'),
  })

  const noAgent = !activeAgentId

  return (
    // Zen collapses to a single, centered writing column — the brief is all that remains; exit focus to
    // review proposals and create. Non-zen keeps the two-column brief + proposals workspace.
    <div className={cx('grid min-h-0 flex-1 gap-4', zen ? 'grid-cols-1' : 'lg:grid-cols-2')}>
      {/* Markdown brief */}
      <div
        className={cx(
          'flex min-h-0 flex-col gap-3 rounded-lg border border-slate-200 bg-white p-4 dark:border-slate-800 dark:bg-slate-900',
          zen && 'mx-auto w-full max-w-3xl',
        )}
      >
        {!zen && (
          <div className="flex items-center gap-2">
            <span className="text-sm font-semibold text-slate-700 dark:text-slate-200">Brief</span>
            {agents.length > 1 && (
              <Select
                value={activeAgentId}
                className="ml-auto max-w-56"
                onChange={(e) => setAgentId(e.target.value)}
              >
                {agents.map((a) => (
                  <option key={a.actorId} value={a.actorId}>{a.displayName} · AI</option>
                ))}
              </Select>
            )}
          </div>
        )}
        <Textarea
          value={markdown}
          onChange={(e) => setMarkdown(e.target.value)}
          placeholder={'Write a freeform markdown brief…\n\n## Add CSV export\nLet users download the board as CSV.\n\n## Fix slow inbox\nThe inbox takes seconds to load with many observations.'}
          className={cx('min-h-0 flex-1 resize-none font-mono leading-relaxed', zen ? 'text-sm sm:text-[15px]' : 'text-[13px]')}
        />
        {!zen && (<>
        <div className="flex items-center gap-3">
          <Button
            variant="default"
            disabled={noAgent || generate.isPending || markdown.trim().length < MIN_MARKDOWN}
            title={noAgent ? 'Add an Agent member to generate proposals' : undefined}
            onClick={() => { lastGenerated.current = ''; runGenerate(markdown) }}
          >
            {generate.isPending ? 'Generating…' : proposals.length ? 'Regenerate' : 'Generate proposals'}
          </Button>
          <label className="flex items-center gap-1.5 text-sm text-slate-600 dark:text-slate-300">
            <input type="checkbox" checked={auto} onChange={(e) => setAuto(e.target.checked)} disabled={noAgent} />
            Auto
          </label>
          {auto && dirty && (
            <span className="text-xs text-slate-400">Auto paused — you’ve edited the proposals. Regenerate to refresh.</span>
          )}
        </div>
        {noAgent && (
          <p className="text-sm text-slate-400">Add an Agent member (with an LLM provider) via Manage project to generate proposals.</p>
        )}
        <ErrorText error={generate.error} />
        </>)}
      </div>

      {/* Proposed change orders — hidden in focus mode; exit focus to review and create. */}
      {!zen && (
      <div className="flex min-h-0 flex-col rounded-lg border border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900">
        <div className="flex items-center gap-2 border-b border-slate-200 px-4 py-2.5 dark:border-slate-800">
          <span className="text-sm font-semibold text-slate-700 dark:text-slate-200">Proposed change orders</span>
          {generate.isPending && <span className="text-xs text-slate-400">Generating…</span>}
          <span className="ml-auto text-xs text-slate-400">{validCount} ready</span>
        </div>

        <div className="min-h-0 flex-1 space-y-2 overflow-auto p-4">
          {proposals.length === 0 ? (
            <p className="text-sm text-slate-400">
              {generate.isPending
                ? 'Generating proposals…'
                : 'Write a brief on the left, then press Generate (or enable Auto) — proposals appear here for you to edit.'}
            </p>
          ) : (
            proposals.map((p, i) => {
              const isEditing = editingId === p.id
              return (
              <div key={p.id} className={cx('rounded-md border border-slate-200 dark:border-slate-700', isEditing ? 'space-y-2 p-3' : 'p-2')}>
                {isEditing ? (
                  <>
                    <div className="flex items-center gap-2">
                      <span className="text-xs font-medium text-slate-400">#{i + 1}</span>
                      <span className="ml-auto" />
                      <Button variant="subtle" aria-label="Collapse change order" onClick={() => setEditingId(null)}>Done</Button>
                      <Button variant="subtle" onClick={() => removeProposal(p.id)}>Delete</Button>
                    </div>
                    <Field label="Title">
                      <TextInput value={p.title} onChange={(e) => update(p.id, { title: e.target.value })} placeholder="Concise, imperative title" />
                    </Field>
                    <Field label="Description">
                      <Textarea value={p.description} onChange={(e) => update(p.id, { description: e.target.value })} />
                    </Field>
                    <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                      <Field label="Kind">
                        <Select value={p.kind} onChange={(e) => update(p.id, { kind: e.target.value as ChangeKind })}>
                          {KINDS.map((k) => <option key={k} value={k}>{k}</option>)}
                        </Select>
                      </Field>
                      <Field label="Priority">
                        <Select value={p.priority} onChange={(e) => update(p.id, { priority: e.target.value as Priority })}>
                          {PRIORITIES.map((pr) => <option key={pr} value={pr}>{pr}</option>)}
                        </Select>
                      </Field>
                    </div>
                  </>
                ) : (
                  // Compact read-only view — dense enough to fit many at once. The read area expands into the
                  // edit form on click; a small delete sits outside that click target (no nested buttons).
                  <div className="flex items-start gap-2">
                    <button type="button" onClick={() => setEditingId(p.id)} className="min-w-0 flex-1 space-y-1 text-left">
                      <p className={cx('text-[13px] font-medium leading-snug', p.title.trim() ? 'text-slate-800 dark:text-slate-100' : 'italic text-slate-400')}>
                        {p.title.trim() || 'Untitled change order'}
                      </p>
                      {p.description.trim() && (
                        <p className="line-clamp-2 whitespace-pre-wrap text-[11px] leading-snug text-slate-500 dark:text-slate-400">{p.description}</p>
                      )}
                      <div className="flex flex-wrap items-center gap-1">
                        <Badge tone={kindTone(p.kind)}>{p.kind}</Badge>
                        {p.priority !== 'None' && <Badge tone="amber">{p.priority}</Badge>}
                      </div>
                    </button>
                    <button
                      type="button"
                      aria-label="Delete change order"
                      onClick={() => removeProposal(p.id)}
                      className="shrink-0 rounded px-1.5 py-0.5 text-xs text-slate-400 hover:bg-slate-100 hover:text-red-600 dark:hover:bg-slate-800 dark:hover:text-red-400"
                    >
                      ✕
                    </button>
                  </div>
                )}
              </div>
              )
            })
          )}
        </div>

        <div className="flex items-center gap-2 border-t border-slate-200 px-4 py-2.5 dark:border-slate-800">
          <Button variant="subtle" onClick={addProposal}>+ Add change order</Button>
          <ErrorText error={confirm.error} />
          <Button
            variant="primary"
            className="ml-auto"
            disabled={confirm.isPending || validCount === 0}
            onClick={() => confirm.mutate()}
          >
            {confirm.isPending ? 'Creating…' : `Create ${validCount} change request${validCount === 1 ? '' : 's'}`}
          </Button>
        </div>
      </div>
      )}
    </div>
  )
}
