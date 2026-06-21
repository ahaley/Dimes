import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router-dom'
import { api } from '../api/client'
import { keys, useAssistConversation, useMe } from '../api/hooks'
import type { ChatBubble } from './AssistChat'
import { ChatBubbles, ChatComposer } from './AssistChat'
import { Badge, Button } from '../components/ui'
import { useToast } from '../components/Toast'

const STATUS_LABEL: Record<string, string> = {
  AwaitingAssistant: 'Awaiting your reply',
  AwaitingRequester: 'Awaiting the requester',
  Closed: 'Closed',
}

/** The assistant's (or requester's) view of a single persisted Capture Assist conversation, reached
 * from the inbox. Shows the requester's draft as read-only context, the thread, and a reply box. */
export function AssistConversationView() {
  const { projectId = '', conversationId = '' } = useParams()
  const navigate = useNavigate()
  const toast = useToast()
  const qc = useQueryClient()
  const { data: me } = useMe()

  const { data: conversation, isLoading, error } = useAssistConversation(projectId, conversationId)
  const [input, setInput] = useState('')

  const post = useMutation({
    mutationFn: (body: string) => api.postAssistMessage(projectId, conversationId, { body }),
    onSuccess: (c) => { qc.setQueryData(keys.assistConversation(c.id), c); setInput('') },
    onError: (e) => toast.error(e instanceof Error ? e.message : 'Could not send your reply'),
  })

  if (isLoading) {
    return <p className="text-sm text-slate-400">Loading conversation…</p>
  }
  if (error || !conversation) {
    return (
      <div className="mx-auto max-w-2xl space-y-3">
        <p className="text-sm text-slate-500">This conversation could not be loaded.</p>
        <Button variant="subtle" onClick={() => navigate(`/projects/${projectId}`)}>Back to board</Button>
      </div>
    )
  }

  const closed = conversation.status === 'Closed'
  const bubbles: ChatBubble[] = conversation.messages.map((m) => ({
    id: m.id,
    // Align to the current viewer: their own messages on the right.
    mine: m.sender === (me?.actorId === conversation.requesterActorId ? 'Requester' : 'Assistant'),
    text: m.body,
  }))

  return (
    <div className="mx-auto flex h-full max-w-4xl flex-col gap-4">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-lg font-semibold text-slate-800 dark:text-slate-100">
            Capture Assist — {conversation.requesterName} → {conversation.assistantName}
          </h1>
          <p className="mt-1 flex items-center gap-2 text-sm text-slate-500">
            {conversation.title && <span className="font-medium text-slate-600 dark:text-slate-300">{conversation.title}</span>}
            <Badge tone={closed ? 'slate' : 'indigo'}>{STATUS_LABEL[conversation.status] ?? conversation.status}</Badge>
          </p>
        </div>
        <Button variant="subtle" onClick={() => navigate(`/projects/${projectId}`)}>Back</Button>
      </div>

      <div className="grid min-h-0 flex-1 gap-4 lg:grid-cols-[1fr_1.4fr]">
        {/* Requester's draft context (read-only) */}
        <div className="min-h-0 overflow-auto rounded-lg border border-slate-200 bg-white p-4 dark:border-slate-800 dark:bg-slate-900">
          <h2 className="text-xs font-semibold uppercase tracking-wide text-slate-400">Draft from {conversation.requesterName}</h2>
          {conversation.draft ? (
            <pre className="mt-2 whitespace-pre-wrap break-words font-sans text-sm text-slate-700 dark:text-slate-200">{conversation.draft}</pre>
          ) : (
            <p className="mt-2 text-sm text-slate-400">No draft was attached.</p>
          )}
        </div>

        {/* Conversation */}
        <div className="flex min-h-0 flex-col rounded-lg border border-slate-200 bg-white dark:border-slate-800 dark:bg-slate-900">
          <ChatBubbles items={bubbles} emptyText="No messages yet." />
          <ChatComposer
            value={input}
            onChange={setInput}
            onSend={() => { const b = input.trim(); if (b) post.mutate(b) }}
            disabled={closed || post.isPending}
            placeholder={closed ? 'This conversation is closed' : 'Write a reply…'}
          />
        </div>
      </div>
    </div>
  )
}
