import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { useInbox, useMe, useProjectInvalidator, useSources } from '../api/hooks'
import type { Observation } from '../api/types'
import { Badge, Button, ErrorText, Field, Modal, Select, TextInput } from '../components/ui'
import { Drawer } from '../components/Drawer'
import { useToast } from '../components/Toast'

export function InboxDrawer({
  projectId, onClose,
}: { projectId: string; onClose: () => void }) {
  const { data: observations } = useInbox(projectId)
  const { data: me } = useMe()
  // Latent signals (no target) are project-wide; directed signals (e.g. assist requests) only show to
  // the addressed actor.
  const open = (observations ?? []).filter(
    (o) => (o.status === 'New' || o.status === 'Clustered') && (!o.targetActorId || o.targetActorId === me?.actorId),
  )
  const [promote, setPromote] = useState<Observation | null>(null)

  return (
    <Drawer title={`Observation inbox (${open.length})`} onClose={onClose}>
      <div className="flex h-full flex-col">
        <p className="mb-3 text-xs text-slate-500">
          Captured signals awaiting triage. Promote the meaningful ones into change requests; dismiss the noise.
        </p>

        <ul className="flex-1 space-y-2">
          {open.map((o) =>
            o.kind === 'AssistRequest' ? (
              <AssistRequestRow key={o.id} observation={o} projectId={projectId} onClose={onClose} />
            ) : (
              <li key={o.id} className="rounded-md border border-slate-200 p-3 dark:border-slate-700">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge tone="amber">{o.kind}</Badge>
                  {o.occurrenceCount > 1 && <Badge tone="red">×{o.occurrenceCount}</Badge>}
                  {o.status === 'Clustered' && <Badge tone="slate">clustered</Badge>}
                </div>
                <p className="mt-2 line-clamp-3 break-words font-mono text-xs text-slate-600 dark:text-slate-300">{o.payload}</p>
                <div className="mt-3 flex flex-wrap gap-2">
                  <Button variant="primary" onClick={() => setPromote(o)}>Promote</Button>
                  <DismissButton id={o.id} projectId={projectId} />
                </div>
              </li>
            ),
          )}
          {open.length === 0 && (
            <li className="rounded-md border border-dashed border-slate-200 p-6 text-center text-sm text-slate-400 dark:border-slate-700">
              Inbox is empty. Captured signals will land here for triage.
            </li>
          )}
        </ul>

        <SimulateCapture projectId={projectId} />
      </div>

      {promote && (
        <PromoteModal
          observation={promote}
          projectId={projectId}
          onClose={() => setPromote(null)}
        />
      )}
    </Drawer>
  )
}

/** A Capture Assist request directed at the current user. Routes into the conversation to reply;
 * Promote/Dismiss don't apply (it's cleared when the assistant answers or the requester closes it). */
function AssistRequestRow({
  observation, projectId, onClose,
}: { observation: Observation; projectId: string; onClose: () => void }) {
  const navigate = useNavigate()
  let info: { conversationId?: string; requesterName?: string; preview?: string } = {}
  try { info = JSON.parse(observation.payload) } catch { /* malformed payload — show generic copy */ }

  return (
    <li className="rounded-md border border-indigo-200 bg-indigo-50/40 p-3 dark:border-indigo-900/60 dark:bg-indigo-950/20">
      <div className="flex flex-wrap items-center gap-2">
        <Badge tone="indigo">assist request</Badge>
        {observation.occurrenceCount > 1 && <Badge tone="slate">{observation.occurrenceCount} updates</Badge>}
      </div>
      <p className="mt-2 text-sm text-slate-700 dark:text-slate-200">
        <span className="font-medium">{info.requesterName ?? 'A teammate'}</span> asked you for help with a change.
      </p>
      {info.preview && <p className="mt-1 line-clamp-3 break-words text-xs text-slate-500 dark:text-slate-400">{info.preview}</p>}
      <div className="mt-3">
        <Button
          variant="primary"
          disabled={!info.conversationId}
          onClick={() => { if (info.conversationId) { onClose(); navigate(`/projects/${projectId}/assist/${info.conversationId}`) } }}
        >
          Open &amp; reply
        </Button>
      </div>
    </li>
  )
}

function DismissButton({ id, projectId }: { id: string; projectId: string }) {
  const invalidate = useProjectInvalidator(projectId)
  const toast = useToast()
  const dismiss = useMutation({
    mutationFn: () => api.dismissObservation(id, 'noise'),
    onSuccess: () => { invalidate(); toast.success('Observation dismissed') },
    onError: (e) => toast.error(e instanceof Error ? e.message : 'Could not dismiss'),
  })
  return <Button variant="subtle" disabled={dismiss.isPending} onClick={() => dismiss.mutate()}>Dismiss</Button>
}

function PromoteModal({
  observation, projectId, onClose,
}: { observation: Observation; projectId: string; onClose: () => void }) {
  const invalidate = useProjectInvalidator(projectId)
  const toast = useToast()
  const [title, setTitle] = useState('')
  const promote = useMutation({
    mutationFn: () => api.promoteObservation(observation.id, { title, description: observation.payload }),
    onSuccess: () => { invalidate(); toast.success('Promoted to a change request'); onClose() },
    onError: (e) => toast.error(e instanceof Error ? e.message : 'Could not promote'),
  })
  return (
    <Modal title="Promote to change request" onClose={onClose}>
      <div className="space-y-3">
        <p className="rounded bg-slate-50 p-2 font-mono text-xs text-slate-600 dark:bg-slate-800 dark:text-slate-300">{observation.payload}</p>
        <Field label="Change title">
          <TextInput value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Summarize the change" autoFocus />
        </Field>
        <ErrorText error={promote.error} />
        <div className="flex justify-end gap-2">
          <Button variant="subtle" onClick={onClose}>Cancel</Button>
          <Button variant="primary" disabled={!title.trim() || promote.isPending} onClick={() => promote.mutate()}>
            Promote
          </Button>
        </div>
      </div>
    </Modal>
  )
}

/** Dev/demo helper: post a signal to a source as the SDK/Seq would. Tucked into a collapsed
 * disclosure so it stays out of the live triage flow. */
function SimulateCapture({ projectId }: { projectId: string }) {
  const { data: sources } = useSources(projectId)
  const invalidate = useProjectInvalidator(projectId)
  const [expanded, setExpanded] = useState(false)
  const [sourceId, setSourceId] = useState('')
  const [message, setMessage] = useState('')
  const [fingerprint, setFingerprint] = useState('')

  const effectiveSource = sourceId || sources?.[0]?.id || ''
  const ingest = useMutation({
    mutationFn: () =>
      api.ingest(effectiveSource, {
        kind: 'ExplicitFeedback',
        payload: JSON.stringify({ message }),
        fingerprint: fingerprint || null,
      }),
    onSuccess: () => { invalidate(); setMessage('') },
  })

  return (
    <details
      open={expanded}
      onToggle={(e) => setExpanded((e.target as HTMLDetailsElement).open)}
      className="mt-3 border-t border-slate-200 pt-3 dark:border-slate-800"
    >
      <summary className="cursor-pointer text-xs font-medium text-slate-500">Dev: simulate capture</summary>
      {!sources || sources.length === 0 ? (
        <p className="mt-2 text-xs text-slate-400">Add an observation source in Manage to simulate capture.</p>
      ) : (
        <div className="mt-2 space-y-2">
          <Select value={effectiveSource} onChange={(e) => setSourceId(e.target.value)}>
            {sources.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
          </Select>
          <TextInput value={message} onChange={(e) => setMessage(e.target.value)} placeholder="signal message" />
          <TextInput value={fingerprint} onChange={(e) => setFingerprint(e.target.value)} placeholder="fingerprint (optional, for aggregation)" />
          <Button variant="default" disabled={!message.trim() || ingest.isPending} onClick={() => ingest.mutate()}>
            Send signal
          </Button>
          <ErrorText error={ingest.error} />
        </div>
      )}
    </details>
  )
}
