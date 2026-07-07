import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router-dom'
import { api } from '../api/client'
import {
  keys,
  useAssistConversation,
  useMe,
  useMembers,
  useMyAssistConversations,
  useProjectInvalidator,
  useProjects,
} from '../api/hooks'
import type { AssistConversationStatus, ChangeKind, ChatTurn, Member, Priority } from '../api/types'
import { Badge, Button, cx, ErrorText, Field, Select, TextInput, Textarea } from '../components/ui'
import { useToast } from '../components/Toast'
import { ChatBubbles, ChatComposer, type ChatBubble } from './AssistChat'
import { CaptureFreestyle } from './CaptureFreestyle'

/** Whether a member can be picked as an assistant: AI agents always; humans need real lifecycle
 * authority (Contributor+) so they can clear the request from their inbox. */
function isEligibleAssistant(m: Member): boolean {
  return m.type === 'Agent' || m.role === 'Contributor' || m.role === 'Maintainer'
}

// Resume-list status copy, from the requester's vantage point.
const RESUME_LABEL: Record<AssistConversationStatus, string> = {
  AwaitingAssistant: 'Awaiting reply',
  AwaitingRequester: 'Your turn',
  Closed: 'Closed',
}
const RESUME_TONE: Record<AssistConversationStatus, string> = {
  AwaitingAssistant: 'amber',
  AwaitingRequester: 'indigo',
  Closed: 'slate',
}

// Remember the requester's last guided/freestyle choice so re-entering capture restores it.
const MODE_KEY = 'dimes.captureAssist.mode'

/**
 * Capture Assist Mode — a full-page (zen) space to grow a loose idea into a change request with the
 * help of an assistant. The assistant can be an AI Agent (ephemeral chat, replayed to the stateless
 * endpoint each turn) or a human teammate (a persisted, two-way conversation that also surfaces in the
 * teammate's observation inbox). Either way, when the user is happy they confirm a title and
 * description and create the change, which lands in the Captured state via the normal endpoint.
 */
export function CaptureAssistView() {
  const { projectId = '', conversationId: routeConversationId } = useParams()
  const navigate = useNavigate()
  const toast = useToast()
  const invalidate = useProjectInvalidator(projectId)
  const qc = useQueryClient()
  const { data: me } = useMe()

  const { data: members } = useMembers(projectId)
  // Cache hit — the app shell already fetches the project list. Used for the focus-workbench label.
  const { data: projects } = useProjects()
  const projectName = projects?.find((p) => p.id === projectId)?.name
  // Candidate assistants: everyone but yourself. Agents drive the AI chat; eligible humans (Contributor+)
  // open a persisted conversation.
  const candidates = useMemo(
    () => (members ?? []).filter((m) => m.actorId !== me?.actorId),
    [members, me?.actorId],
  )
  const agents = useMemo(() => candidates.filter((m) => m.type === 'Agent'), [candidates])
  // Default to an agent with the Assistant role; otherwise the first agent; otherwise the first eligible human.
  const defaultAssistantId =
    (agents.find((a) => a.role === 'Assistant') ?? agents[0])?.actorId
    ?? candidates.find(isEligibleAssistant)?.actorId
    ?? ''
  const [assistantId, setAssistantId] = useState('')
  const activeAssistantId = assistantId || defaultAssistantId
  const activeAssistant = candidates.find((m) => m.actorId === activeAssistantId)
  const isHuman = activeAssistant?.type === 'Human'

  // Guided (chat) vs Freestyle (markdown brief → batch of proposals). Freestyle has no persisted
  // conversation, so resuming one forces guided. Seed from the last remembered choice.
  const [mode, setMode] = useState<'guided' | 'freestyle'>(() => {
    try {
      const saved = localStorage.getItem(MODE_KEY)
      return saved === 'freestyle' || saved === 'guided' ? saved : 'guided'
    } catch { return 'guided' }
  })
  // Zen (focus) mode strips the page chrome so the writer can concentrate on the brief. It only
  // applies to freestyle; `inZen` (below) folds in that gate so guided can never get stuck in zen.
  const [zen, setZen] = useState(false)

  // The draft being shaped.
  const [rough, setRough] = useState('')
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [kind, setKind] = useState<ChangeKind>('Feature')
  const [priority, setPriority] = useState<Priority>('None')

  // The conversation. AI: ephemeral turns held here. Human: a persisted conversation by id.
  const [messages, setMessages] = useState<ChatTurn[]>([])
  const [input, setInput] = useState('')
  // Seed from the route (resume) so a reload/bookmark of /capture/:id reopens that conversation.
  const [conversationId, setConversationId] = useState<string | null>(routeConversationId ?? null)
  const { data: conversation } = useAssistConversation(projectId, conversationId ?? undefined)

  // Conversations this user started, surfaced as a "pick up where you left off" list when idle.
  const { data: myConversations } = useMyAssistConversations(projectId)
  const resumable = useMemo(
    () => (myConversations ?? []).filter((c) => c.status !== 'Closed' && c.id !== conversationId),
    [myConversations, conversationId],
  )

  // Follow the route param: a resume link (or browser back/forward) swaps the active conversation
  // without remounting, so keep local state in lockstep.
  useEffect(() => {
    setConversationId(routeConversationId ?? null)
  }, [routeConversationId])

  // Resume hydration: when a persisted conversation loads, lock the picker to its assistant and seed
  // the title/draft once, without clobbering anything the user has since typed.
  const [hydratedId, setHydratedId] = useState<string | null>(null)
  useEffect(() => {
    if (!conversation || conversation.id === hydratedId) return
    setAssistantId(conversation.assistantActorId)
    setTitle((t) => t || conversation.title || '')
    setRough((r) => r || conversation.draft || '')
    setHydratedId(conversation.id)
  }, [conversation, hydratedId])

  const composeDraft = () =>
    [
      rough && `Rough draft:\n${rough}`,
      title && `Proposed title: ${title}`,
      description && `Proposed description:\n${description}`,
    ]
      .filter(Boolean)
      .join('\n\n') || null

  // AI assistant: stateless chat replayed each turn.
  const aiChat = useMutation({
    mutationFn: (next: ChatTurn[]) =>
      api.captureAssistChat(projectId, { agentActorId: activeAssistantId, draft: composeDraft(), messages: next }),
    onSuccess: (res) => setMessages((m) => [...m, { role: 'assistant', content: res.reply }]),
    onError: (e) => {
      // Roll the optimistic user turn back so they can retry/edit.
      setMessages((m) => (m[m.length - 1]?.role === 'user' ? m.slice(0, -1) : m))
      toast.error(e instanceof Error ? e.message : 'The assistant could not respond')
    },
  })

  // Stash the last attempted text so an error handler can restore it for retry.
  const [lastSent, setLastSent] = useState('')

  // Human assistant: persisted, bubbled into their inbox.
  const startHuman = useMutation({
    mutationFn: (message: string) =>
      api.startAssistConversation(projectId, {
        assistantActorId: activeAssistantId,
        draft: composeDraft(),
        title: title || null,
        message,
      }),
    onSuccess: (c) => {
      setConversationId(c.id)
      qc.setQueryData(keys.assistConversation(c.id), c)
      // Make the now-persisted conversation reload-safe and shareable, and mark it hydrated so the
      // resume effect doesn't overwrite the draft the user is actively shaping.
      setHydratedId(c.id)
      navigate(`/projects/${projectId}/capture/${c.id}`, { replace: true })
    },
    onError: (e) => { setInput((i) => i || lastSent); toast.error(e instanceof Error ? e.message : 'Could not send to the assistant') },
  })
  const postHuman = useMutation({
    mutationFn: (message: string) => api.postAssistMessage(projectId, conversationId!, { body: message }),
    onSuccess: (c) => qc.setQueryData(keys.assistConversation(c.id), c),
    onError: (e) => { setInput((i) => i || lastSent); toast.error(e instanceof Error ? e.message : 'Could not send your message') },
  })

  const send = () => {
    const content = input.trim()
    if (!content || !activeAssistantId) return
    setLastSent(content)
    if (isHuman) {
      setInput('')
      if (!conversationId) startHuman.mutate(content)
      else postHuman.mutate(content)
    } else {
      const next: ChatTurn[] = [...messages, { role: 'user', content }]
      setMessages(next)
      setInput('')
      aiChat.mutate(next)
    }
  }

  const create = useMutation({
    mutationFn: () =>
      api.createChange(projectId, { title: title.trim(), description: description || null, kind, priority }),
    onSuccess: async (change) => {
      // Close + link the conversation to the change it produced (best-effort).
      if (conversationId) {
        try { await api.closeAssistConversation(projectId, conversationId, { changeRequestId: change.id }) } catch { /* non-fatal */ }
      }
      invalidate()
      toast.success('Change request captured')
      navigate(`/projects/${projectId}/changes/${change.id}`)
    },
    onError: (e) => toast.error(e instanceof Error ? e.message : 'Could not create the change'),
  })

  // Bubbles + pending state differ by assistant kind.
  const bubbles: ChatBubble[] = isHuman
    ? (conversation?.messages ?? []).map((m) => ({ id: m.id, mine: m.sender === 'Requester', text: m.body }))
    : messages.map((m, i) => ({ id: String(i), mine: m.role === 'user', text: m.content }))

  const sending = aiChat.isPending || startHuman.isPending || postHuman.isPending
  const closed = isHuman && conversation?.status === 'Closed'
  const noAssistant = !activeAssistantId
  // Resuming a persisted conversation forces guided (freestyle has nothing to resume).
  const effectiveMode = conversationId ? 'guided' : mode
  // Zen only bites in freestyle; deriving it here means switching to guided auto-drops zen with no reset.
  const inZen = effectiveMode === 'freestyle' && zen

  // Keyboard: Esc exits focus mode; Cmd/Ctrl+. toggles it. Only wired while freestyle is active.
  useEffect(() => {
    if (effectiveMode !== 'freestyle') return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape' && zen) setZen(false)
      else if ((e.metaKey || e.ctrlKey) && e.key === '.') { e.preventDefault(); setZen((z) => !z) }
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [effectiveMode, zen])

  const footer = (() => {
    if (!isHuman) return aiChat.isPending ? <p className="text-sm text-slate-400">Assistant is thinking…</p> : null
    if (sending) return <p className="text-sm text-slate-400">Sending…</p>
    if (conversation?.status === 'AwaitingAssistant')
      return <p className="text-sm text-slate-400">Waiting for {activeAssistant?.displayName} to reply…</p>
    return null
  })()

  const emptyText = noAssistant
    ? 'Add an assistant (an AI agent, or a teammate) via Manage project to chat here.'
    : isHuman
      ? `Send a message to loop ${activeAssistant?.displayName} in — they'll see it in their inbox and reply here.`
      : 'Start by describing your idea — the assistant will help you refine it.'

  return (
    <div className="mx-auto flex h-full max-w-6xl flex-col gap-4">
      {/* Page header — hidden in focus mode; the fullscreen workbench (in CaptureFreestyle) carries
          its own exit affordance. */}
      {!inZen && (
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-lg font-semibold text-slate-800 dark:text-slate-100">Capture Assist</h1>
          <p className="mt-1 text-sm text-slate-500">
            {effectiveMode === 'freestyle'
              ? 'Write a freeform markdown brief and turn it into a batch of change requests you can edit before creating.'
              : 'Talk an idea through with an AI agent or a teammate, then confirm a title and description to capture it as a change request.'}
          </p>
        </div>
        <div className="flex items-center gap-2">
          {/* Mode chooser — hidden while resuming a (guided-only) persisted conversation. */}
          {!conversationId && (
            <div className="flex rounded-md border border-slate-200 p-0.5 dark:border-slate-700">
              {(['guided', 'freestyle'] as const).map((m) => (
                <button
                  key={m}
                  type="button"
                  onClick={() => { setMode(m); try { localStorage.setItem(MODE_KEY, m) } catch { /* non-fatal */ } }}
                  className={cx(
                    'rounded px-3 py-1 text-sm font-medium capitalize transition-colors',
                    effectiveMode === m
                      ? 'bg-indigo-600 text-white dark:bg-indigo-500'
                      : 'text-slate-500 hover:text-slate-700 dark:hover:text-slate-200',
                  )}
                >
                  {m}
                </button>
              ))}
            </div>
          )}
          {effectiveMode === 'freestyle' && (
            <Button
              variant="subtle"
              aria-pressed={zen}
              aria-label="Enter focus mode"
              title="Focus mode — hide distractions (⌘/Ctrl+. or Esc to exit)"
              onClick={() => setZen(true)}
            >
              Focus
            </Button>
          )}
          <Button variant="subtle" onClick={() => navigate(`/projects/${projectId}`)}>Exit</Button>
        </div>
      </div>
      )}

      {effectiveMode === 'freestyle' && (
        <CaptureFreestyle
          projectId={projectId}
          projectName={projectName}
          agents={agents}
          zen={inZen}
          onExitZen={() => setZen(false)}
        />
      )}

      {effectiveMode === 'guided' && !conversationId && resumable.length > 0 && (
        <div className="rounded-lg border border-slate-200 bg-slate-50 p-3 dark:border-slate-800 dark:bg-slate-900/60">
          <h2 className="text-xs font-semibold uppercase tracking-wide text-slate-400">Pick up where you left off</h2>
          <ul className="mt-2 space-y-1.5">
            {resumable.map((c) => (
              <li key={c.id}>
                <button
                  onClick={() => navigate(`/projects/${projectId}/capture/${c.id}`)}
                  className="flex w-full items-center gap-2 rounded-md border border-slate-200 bg-white px-3 py-2 text-left text-sm hover:border-indigo-300 hover:bg-indigo-50/40 dark:border-slate-700 dark:bg-slate-900 dark:hover:border-indigo-800 dark:hover:bg-indigo-950/20"
                >
                  <span className="min-w-0 flex-1 truncate">
                    <span className="font-medium text-slate-700 dark:text-slate-200">{c.title || 'Untitled idea'}</span>
                    <span className="text-slate-400"> · with {c.assistantName}</span>
                    {c.lastMessagePreview && (
                      <span className="ml-2 text-xs text-slate-400">— {c.lastMessagePreview}</span>
                    )}
                  </span>
                  <Badge tone={RESUME_TONE[c.status]}>{RESUME_LABEL[c.status]}</Badge>
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}

      {effectiveMode === 'guided' && (
      <div className="grid min-h-0 flex-1 gap-4 lg:grid-cols-2">
        {/* Draft form */}
        <div className="flex min-h-0 flex-col gap-3 overflow-auto rounded-lg border border-slate-200 bg-white p-4 dark:border-slate-800 dark:bg-slate-900">
          <Field label="Rough draft of the problem">
            <Textarea
              value={rough}
              onChange={(e) => setRough(e.target.value)}
              placeholder="Describe the loose idea, problem, or friction you noticed…"
              className="min-h-28"
            />
          </Field>
          <Field label="Title">
            <TextInput value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Add CSV export" />
          </Field>
          <Field label="Description">
            <Textarea value={description} onChange={(e) => setDescription(e.target.value)} className="min-h-28" />
          </Field>
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            <Field label="Kind">
              {/* Observation-driven is set only by promoting an observation, never picked manually. */}
              <Select value={kind} onChange={(e) => setKind(e.target.value as ChangeKind)}>
                <option value="Feature">Feature</option>
                <option value="Problem">Problem</option>
              </Select>
            </Field>
            <Field label="Priority">
              <Select value={priority} onChange={(e) => setPriority(e.target.value as Priority)}>
                {['None', 'Low', 'Medium', 'High', 'Critical'].map((p) => <option key={p} value={p}>{p}</option>)}
              </Select>
            </Field>
          </div>
          <ErrorText error={create.error} />
          <div className="mt-auto flex items-center justify-end gap-2 pt-2">
            <Button
              variant="primary"
              disabled={!title.trim() || create.isPending}
              title={!title.trim() ? 'Confirm a title to capture' : 'Create the change in the Captured state'}
              onClick={() => create.mutate()}
            >
              Create change request
            </Button>
          </div>
        </div>

        {/* Conversation */}
        <div className="flex min-h-0 flex-col rounded-lg border border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900">
          <div className="flex items-center gap-2 border-b border-slate-200 px-4 py-2.5 dark:border-slate-800">
            <span className="text-sm font-semibold text-slate-700 dark:text-slate-200">Assistant</span>
            {candidates.length > 0 ? (
              <Select
                value={activeAssistantId}
                className="ml-auto max-w-56"
                // Once a human conversation is underway, lock the picker to that teammate.
                disabled={!!conversationId}
                onChange={(e) => setAssistantId(e.target.value)}
              >
                {candidates.map((a) => (
                  <option key={a.actorId} value={a.actorId} disabled={!isEligibleAssistant(a)}>
                    {a.displayName}
                    {a.type === 'Agent' ? ' · AI' : isEligibleAssistant(a) ? ' · teammate' : ' · teammate (needs Contributor)'}
                  </option>
                ))}
              </Select>
            ) : (
              <Badge tone="amber">no assistant</Badge>
            )}
          </div>

          <ChatBubbles items={bubbles} emptyText={emptyText} footer={footer} />

          <ChatComposer
            value={input}
            onChange={setInput}
            onSend={send}
            disabled={noAssistant || sending || closed}
            placeholder={
              noAssistant ? 'No assistant available'
                : closed ? 'This conversation is closed'
                  : isHuman ? `Message ${activeAssistant?.displayName}…`
                    : 'Message the assistant…'
            }
          />
        </div>
      </div>
      )}
    </div>
  )
}
