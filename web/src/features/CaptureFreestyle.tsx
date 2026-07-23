import { useEffect, useMemo, useRef, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { useProjectInvalidator, useProjects } from '../api/hooks'
import type { ChangeKind, Member, Priority } from '../api/types'
import { Badge, Button, cx, ErrorText, Field, Select, TextInput, Textarea } from '../components/ui'
import { useToast } from '../components/Toast'
import { kindTone } from '../lifecycle'

// Observation-driven is provenance-only (applied by promotion), so it's not a manually selectable kind.
const KINDS: ChangeKind[] = ['Feature', 'Problem', 'Epic', 'Chore']
const PRIORITIES: Priority[] = ['None', 'Low', 'Medium', 'High', 'Critical']
const DEBOUNCE_MS = 1200
const MIN_MARKDOWN = 8 // skip generation until the brief has some substance

// The freestyle brief lives only in local component state and is never persisted server-side until the
// user confirms the batch. Stash it in localStorage (per project) so navigating away — a route change,
// a mode switch, or a full reload — and coming back restores the in-progress brief instead of losing it.
const freestyleDraftKey = (projectId: string) => `dimes.captureAssist.freestyle.${projectId}`

// Client-only editable proposal. `id` is a stable React key; it is never sent to the create endpoint.
// `projectId` is the project the proposal will be captured into — the one being briefed unless the
// user redirects the card elsewhere.
type Proposal = {
  id: string; title: string; description: string; kind: ChangeKind; priority: Priority; projectId: string
}

/**
 * Capture Assist — Freestyle Mode. The user writes a freeform markdown brief on the left; an Agent's
 * LLM decomposes it into a list of proposed change orders on the right, which the user can edit, add to,
 * or delete. Generation runs on demand via the Generate button, or — when the user opts in with the
 * Auto toggle — automatically a short while after they stop typing. Confirming creates them all in the
 * Captured state in one batch. Recommend-only: nothing is created until the user confirms.
 */
export function CaptureFreestyle({ projectId, projectName, agents, zen = false, onExitZen }: {
  projectId: string; projectName?: string; agents: Member[]; zen?: boolean; onExitZen?: () => void
}) {
  const navigate = useNavigate()
  const toast = useToast()
  const invalidate = useProjectInvalidator(projectId)

  // Projects a proposal can be redirected to. Same args as the app shell's call so this shares its
  // cached query instead of issuing a second /api/projects fetch. A redirect target must be one the
  // user has real capture authority in — Contributor or higher — so a Reporter-only (or, for a site
  // admin, non-member) project is never offered. Archived projects are dropped too (you can't capture
  // into one), and the briefed project always leads the list as the default: the user is already
  // capturing into it, so it stays available whatever their role there.
  const { data: allProjects } = useProjects(true, true)
  const targets = useMemo(() => {
    const active = (allProjects ?? []).filter((p) => !p.isArchived)
    const current = active.find((p) => p.id === projectId)
    const rest = active
      .filter((p) => p.id !== projectId && (p.myRole === 'Contributor' || p.myRole === 'Maintainer'))
      .sort((a, b) => a.name.localeCompare(b.name))
    return [...(current ? [current] : []), ...rest]
  }, [allProjects, projectId])
  const targetName = (id: string) => targets.find((p) => p.id === id)?.name ?? 'another project'

  const defaultAgentId =
    (agents.find((a) => a.role === 'Assistant') ?? agents[0])?.actorId ?? ''
  const [agentId, setAgentId] = useState('')
  const activeAgentId = agentId || defaultAgentId

  const draftKey = freestyleDraftKey(projectId)
  const [markdown, setMarkdown] = useState(() => {
    try { return localStorage.getItem(draftKey) ?? '' } catch { return '' }
  })
  const [proposals, setProposals] = useState<Proposal[]>([])
  // Which proposal is expanded into its full edit form; null means every card is compact. Editing one at
  // a time keeps the list scannable — the compact default is the read-only presentation.
  const [editingId, setEditingId] = useState<string | null>(null)
  const [auto, setAuto] = useState(false)
  // Once the user hand-edits the proposals, freeze auto-generation so it never clobbers their work —
  // they must press Regenerate to refresh from the brief.
  const [dirty, setDirty] = useState(false)
  // Markdown text of the last dispatched generation, so we never re-request unchanged input.
  const lastGenerated = useRef('')
  // Set when the user hand-edits proposals while a generation is in flight. The dirty gate only stops
  // *dispatching* while dirty; edits made after dispatch would otherwise be clobbered on arrival.
  const editedSinceDispatch = useRef(false)

  const generate = useMutation({
    mutationFn: (md: string) => api.generateProposals(projectId, { agentActorId: activeAgentId, markdown: md }),
    onSuccess: (res) => {
      // The user edited while this request was in flight — their work wins; drop the stale result.
      // (dirty is already true, so auto stays frozen until they explicitly Regenerate.)
      if (editedSinceDispatch.current) return
      setProposals(res.proposals.map((p) => ({
        id: crypto.randomUUID(),
        title: p.title,
        description: p.description ?? '',
        kind: p.kind,
        priority: p.priority,
        projectId,
      })))
      // The prior proposals (and any open editor) are gone — collapse so editingId can't dangle.
      setEditingId(null)
      setDirty(false)
    },
  })

  const runGenerate = (md: string) => {
    if (!activeAgentId || generate.isPending) return
    if (md.trim().length < MIN_MARKDOWN) return
    if (md === lastGenerated.current) return
    lastGenerated.current = md
    editedSinceDispatch.current = false
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

  const update = (id: string, patch: Partial<Proposal>) => {
    setProposals((ps) => ps.map((p) => (p.id === id ? { ...p, ...patch } : p)))
    setDirty(true)
    editedSinceDispatch.current = true
  }
  const addProposal = () => {
    const id = crypto.randomUUID()
    setProposals((ps) => [...ps, { id, title: '', description: '', kind: 'Feature', priority: 'None', projectId }])
    // A brand-new card has nothing to read, so open it straight into edit mode.
    setEditingId(id)
    setDirty(true)
    editedSinceDispatch.current = true
  }
  const removeProposal = (id: string) => {
    setProposals((ps) => ps.filter((p) => p.id !== id))
    setEditingId((cur) => (cur === id ? null : cur))
    setDirty(true)
    editedSinceDispatch.current = true
  }

  const validCount = useMemo(() => proposals.filter((p) => p.title.trim()).length, [proposals])

  // Ids of the proposals a confirm has already created, so a partial failure (one project's batch
  // rejected after an earlier one landed) can drop them before the user retries — otherwise the
  // retry would create them a second time.
  const createdIds = useRef(new Set<string>())

  const confirm = useMutation({
    mutationFn: async () => {
      createdIds.current = new Set()
      // The batch endpoint is per-project, so a redirected card means one call per target. Group by
      // project, briefed project first, so the common path is created before any redirect.
      const groups = new Map<string, Proposal[]>()
      for (const p of proposals.filter((p) => p.title.trim())) {
        const group = groups.get(p.projectId)
        if (group) group.push(p)
        else groups.set(p.projectId, [p])
      }
      const ordered = [...groups].sort(([a], [b]) => (a === projectId ? -1 : b === projectId ? 1 : 0))

      let count = 0
      for (const [target, group] of ordered) {
        const created = await api.createChangesBatch(target, {
          changes: group.map((p) => ({
            title: p.title.trim(),
            description: p.description.trim() || null,
            kind: p.kind,
            priority: p.priority,
          })),
        })
        count += created.length
        group.forEach((p) => createdIds.current.add(p.id))
      }
      return { count, projects: ordered.length }
    },
    onSuccess: ({ count, projects }) => {
      // The brief has done its job — drop the persisted draft so it doesn't resurrect on return.
      try { localStorage.removeItem(draftKey) } catch { /* non-fatal */ }
      invalidate()
      toast.success(
        `${count} change request${count === 1 ? '' : 's'} captured`
        + (projects > 1 ? ` across ${projects} projects` : ''),
      )
      navigate(`/projects/${projectId}`)
    },
    onError: (e) => {
      // Keep only what didn't get created; those are what a retry should send.
      if (createdIds.current.size > 0) {
        setProposals((ps) => ps.filter((p) => !createdIds.current.has(p.id)))
        setEditingId((cur) => (cur !== null && createdIds.current.has(cur) ? null : cur))
        invalidate()
      }
      toast.error(e instanceof Error ? e.message : 'Could not create the changes')
    },
  })

  const noAgent = !activeAgentId

  // Focus-mode dialog behavior. The overlay visually covers the app chrome but the chrome stays in the
  // DOM and tabbable, so contain Tab inside the workbench (Shift+Tab must not land on the invisible
  // "Sign out") and move focus to the writing surface on entry.
  const rootRef = useRef<HTMLDivElement>(null)
  const briefRef = useRef<HTMLTextAreaElement>(null)
  useEffect(() => {
    if (zen) briefRef.current?.focus()
  }, [zen])
  const trapTab = (e: React.KeyboardEvent) => {
    if (!zen || e.key !== 'Tab' || !rootRef.current) return
    const focusables = Array.from(
      rootRef.current.querySelectorAll<HTMLElement>(
        'button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), a[href], [tabindex]:not([tabindex="-1"])',
      ),
    )
    if (focusables.length === 0) return
    const first = focusables[0]
    const last = focusables[focusables.length - 1]
    const active = document.activeElement
    if (e.shiftKey && (active === first || !rootRef.current.contains(active))) {
      e.preventDefault()
      last.focus()
    } else if (!e.shiftKey && (active === last || !rootRef.current.contains(active))) {
      e.preventDefault()
      first.focus()
    }
  }

  return (
    // Focus (zen) turns the workspace into a fullscreen workbench: a fixed overlay that covers the app
    // chrome (sidebar + toolbar — z-40 sits above the mobile drawer scrim, below modals) with the brief
    // and the proposed change orders side by side. Non-zen is the same two panels inside the normal page.
    // The overlay classes live on this root (not a conditional wrapper in the parent) so the component
    // never remounts on toggle — the client-only proposals state must survive entering/exiting focus.
    <div
      ref={rootRef}
      role={zen ? 'dialog' : undefined}
      aria-modal={zen ? true : undefined}
      aria-label={zen ? 'Focus mode — freestyle capture' : undefined}
      onKeyDown={zen ? trapTab : undefined}
      className={cx(
        'grid grid-cols-1 lg:grid-cols-2',
        zen
          ? 'fixed inset-0 z-40 grid-rows-[auto_minmax(0,1fr)_minmax(0,1fr)] gap-3 bg-slate-50 p-3 sm:p-4 lg:grid-rows-[auto_minmax(0,1fr)] dark:bg-slate-950'
          : 'min-h-0 flex-1 gap-4',
      )}
    >
      {zen && (
        <div className="col-span-full flex items-center justify-between">
          {/* Name the project so the writer always knows whose backlog they're feeding. */}
          <span className="text-xs font-medium uppercase tracking-wide text-slate-400">
            Focus · Capture Assist{projectName ? <> · <span className="text-slate-500 dark:text-slate-300">{projectName}</span></> : null}
          </span>
          <Button variant="default" aria-label="Exit focus mode" title="Exit focus mode (Esc)" onClick={onExitZen}>
            Exit focus
          </Button>
        </div>
      )}
      {/* Markdown brief */}
      <div className="flex min-h-0 flex-col gap-3 rounded-lg border border-slate-200 bg-white p-4 dark:border-slate-800 dark:bg-slate-900">
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
          ref={briefRef}
          value={markdown}
          onChange={(e) => setMarkdown(e.target.value)}
          placeholder={'Write a freeform markdown brief…\n\n## Add CSV export\nLet users download the board as CSV.\n\n## Fix slow inbox\nThe inbox takes seconds to load with many observations.'}
          className={cx('min-h-0 flex-1 resize-none font-mono leading-relaxed', zen ? 'text-sm sm:text-[15px]' : 'text-[13px]')}
        />
        {/* Generation is one of focus mode's two verbs (generate + create), so this row stays in zen. */}
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
        {/* Cap the error area: in focus mode the panel lives in a fixed overlay with no page scroll,
            so a verbose provider error must scroll here rather than push content past the viewport. */}
        <div className="max-h-24 shrink-0 overflow-y-auto">
          <ErrorText error={generate.error} />
        </div>
      </div>

      {/* Proposed change orders — in focus mode the panel stays (it's half the workbench) but sheds its
          header; the ready count already lives in the Create button label. */}
      <div className="flex min-h-0 flex-col rounded-lg border border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900">
        {!zen && (
          <div className="flex items-center gap-2 border-b border-slate-200 px-4 py-2.5 dark:border-slate-800">
            <span className="text-sm font-semibold text-slate-700 dark:text-slate-200">Proposed change orders</span>
            {generate.isPending && <span className="text-xs text-slate-400">Generating…</span>}
            <span className="ml-auto text-xs text-slate-400">{validCount} ready</span>
          </div>
        )}

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
                    {/* Redirect: send this one proposal to a different project. Only worth a control
                        when there's somewhere else to send it. */}
                    {targets.length > 1 && (
                      <Field label="Project">
                        <Select value={p.projectId} onChange={(e) => update(p.id, { projectId: e.target.value })}>
                          {targets.map((t) => (
                            <option key={t.id} value={t.id}>{t.name}{t.id === projectId ? ' (this project)' : ''}</option>
                          ))}
                        </Select>
                      </Field>
                    )}
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
                        // text-xs + normal leading + one contrast step over the old 11px slate-400 —
                        // the preview stays compact but reads comfortably, especially in dark mode.
                        <p className="line-clamp-2 whitespace-pre-wrap text-xs leading-normal text-slate-600 dark:text-slate-300">{p.description}</p>
                      )}
                      <div className="flex flex-wrap items-center gap-1">
                        <Badge tone={kindTone(p.kind)}>{p.kind}</Badge>
                        {p.priority !== 'None' && <Badge tone="amber">{p.priority}</Badge>}
                        {/* A redirect changes where this lands, so surface it without opening the card. */}
                        {p.projectId !== projectId && <Badge tone="violet">→ {targetName(p.projectId)}</Badge>}
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
    </div>
  )
}
