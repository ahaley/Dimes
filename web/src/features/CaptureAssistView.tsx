import { useMemo, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router-dom'
import { api } from '../api/client'
import { useMembers, useProjectInvalidator } from '../api/hooks'
import type { ChangeKind, ChatTurn, Priority } from '../api/types'
import { Badge, Button, ErrorText, Field, Select, TextInput, Textarea } from '../components/ui'
import { useToast } from '../components/Toast'

/**
 * Capture Assist Mode — a full-page (zen) space to grow a loose idea into a change request with the
 * help of an AI Assistant agent. The conversation is ephemeral (held here in component state and
 * replayed to the stateless chat endpoint each turn). When the user is happy, they confirm a title
 * and description and create the change, which lands in the Captured state via the normal endpoint.
 */
export function CaptureAssistView() {
  const { projectId = '' } = useParams()
  const navigate = useNavigate()
  const toast = useToast()
  const invalidate = useProjectInvalidator(projectId)

  const { data: members } = useMembers(projectId)
  const agents = useMemo(() => (members ?? []).filter((m) => m.type === 'Agent'), [members])
  // Default to an agent assigned the Assistant role; otherwise the first available agent.
  const defaultAgentId = (agents.find((a) => a.role === 'Assistant') ?? agents[0])?.actorId ?? ''
  const [agentId, setAgentId] = useState('')
  const activeAgentId = agentId || defaultAgentId

  // The draft being shaped.
  const [rough, setRough] = useState('')
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [kind, setKind] = useState<ChangeKind>('Feature')
  const [priority, setPriority] = useState<Priority>('None')

  // The conversation.
  const [messages, setMessages] = useState<ChatTurn[]>([])
  const [input, setInput] = useState('')

  const composeDraft = () =>
    [
      rough && `Rough draft:\n${rough}`,
      title && `Proposed title: ${title}`,
      description && `Proposed description:\n${description}`,
    ]
      .filter(Boolean)
      .join('\n\n') || null

  const chat = useMutation({
    mutationFn: (next: ChatTurn[]) =>
      api.captureAssistChat(projectId, { agentActorId: activeAgentId, draft: composeDraft(), messages: next }),
    onSuccess: (res) => setMessages((m) => [...m, { role: 'assistant', content: res.reply }]),
    onError: (e) => {
      // Roll the optimistic user turn back so they can retry/edit.
      setMessages((m) => (m[m.length - 1]?.role === 'user' ? m.slice(0, -1) : m))
      toast.error(e instanceof Error ? e.message : 'The assistant could not respond')
    },
  })

  const send = () => {
    const content = input.trim()
    if (!content || !activeAgentId) return
    const next: ChatTurn[] = [...messages, { role: 'user', content }]
    setMessages(next)
    setInput('')
    chat.mutate(next)
  }

  const create = useMutation({
    mutationFn: () =>
      api.createChange(projectId, { title: title.trim(), description: description || null, kind, priority }),
    onSuccess: (change) => {
      invalidate()
      toast.success('Change request captured')
      navigate(`/projects/${projectId}/changes/${change.id}`)
    },
    onError: (e) => toast.error(e instanceof Error ? e.message : 'Could not create the change'),
  })

  return (
    <div className="mx-auto flex h-full max-w-6xl flex-col gap-4">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-lg font-semibold text-slate-800 dark:text-slate-100">Capture Assist</h1>
          <p className="mt-1 text-sm text-slate-500">
            Talk an idea through with an AI assistant, then confirm a title and description to capture it
            as a change request.
          </p>
        </div>
        <Button variant="subtle" onClick={() => navigate(`/projects/${projectId}`)}>Exit</Button>
      </div>

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
          <div className="grid grid-cols-2 gap-2">
            <Field label="Kind">
              <Select value={kind} onChange={(e) => setKind(e.target.value as ChangeKind)}>
                <option value="Feature">Feature</option>
                <option value="Problem">Problem</option>
                <option value="ObservationDriven">Observation-driven</option>
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
            {agents.length > 0 ? (
              <Select
                value={activeAgentId}
                className="ml-auto max-w-48"
                onChange={(e) => setAgentId(e.target.value)}
              >
                {agents.map((a) => (
                  <option key={a.actorId} value={a.actorId}>
                    {a.displayName}{a.role === 'Assistant' ? ' · Assistant' : ''}
                  </option>
                ))}
              </Select>
            ) : (
              <Badge tone="amber">no agent</Badge>
            )}
          </div>

          <div className="min-h-0 flex-1 space-y-3 overflow-auto p-4">
            {messages.length === 0 && (
              <p className="text-sm text-slate-400">
                {agents.length === 0
                  ? 'Add an Agent member with an LLM provider (Manage project) to chat here.'
                  : 'Start by describing your idea — the assistant will help you refine it.'}
              </p>
            )}
            {messages.map((m, i) => (
              <div key={i} className={m.role === 'user' ? 'text-right' : 'text-left'}>
                <div
                  className={
                    m.role === 'user'
                      ? 'inline-block max-w-[85%] whitespace-pre-wrap rounded-lg bg-indigo-600 px-3 py-2 text-left text-sm text-white'
                      : 'inline-block max-w-[85%] whitespace-pre-wrap rounded-lg bg-slate-100 px-3 py-2 text-sm text-slate-800 dark:bg-slate-800 dark:text-slate-100'
                  }
                >
                  {m.content}
                </div>
              </div>
            ))}
            {chat.isPending && <p className="text-sm text-slate-400">Assistant is thinking…</p>}
          </div>

          <div className="border-t border-slate-200 p-3 dark:border-slate-800">
            <div className="flex items-end gap-2">
              <Textarea
                value={input}
                onChange={(e) => setInput(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault()
                    send()
                  }
                }}
                placeholder={agents.length === 0 ? 'No assistant available' : 'Message the assistant…'}
                className="min-h-10"
                disabled={agents.length === 0 || chat.isPending}
              />
              <Button
                variant="primary"
                disabled={!input.trim() || !activeAgentId || chat.isPending}
                onClick={send}
              >
                Send
              </Button>
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}
