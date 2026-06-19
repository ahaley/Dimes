import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { api } from '../api/client'
import { useInbox, useProjectInvalidator, useSources } from '../api/hooks'
import type { Observation } from '../api/types'
import { Badge, Button, Card, ErrorText, Field, Modal, Select, TextInput } from '../components/ui'

export function Inbox({ projectId, actingActorId }: { projectId: string; actingActorId: string }) {
  const { data: observations } = useInbox(projectId) // all statuses; we show New + Clustered
  const open = (observations ?? []).filter((o) => o.status === 'New' || o.status === 'Clustered')
  const [promote, setPromote] = useState<Observation | null>(null)

  return (
    <Card className="p-4">
      <div className="mb-3 flex items-center justify-between">
        <h2 className="text-sm font-semibold text-slate-700">Observation inbox</h2>
        <Badge tone="slate">{open.length}</Badge>
      </div>

      <SimulateCapture projectId={projectId} />

      <ul className="mt-3 space-y-2">
        {open.map((o) => (
          <li key={o.id} className="rounded-md border border-slate-200 p-2.5">
            <div className="flex items-center gap-2">
              <Badge tone="amber">{o.kind}</Badge>
              {o.occurrenceCount > 1 && <Badge tone="red">×{o.occurrenceCount}</Badge>}
              {o.status === 'Clustered' && <Badge tone="slate">clustered</Badge>}
            </div>
            <p className="mt-1 line-clamp-2 break-words font-mono text-xs text-slate-600">{o.payload}</p>
            <div className="mt-2 flex gap-2">
              <Button variant="primary" onClick={() => setPromote(o)}>Promote</Button>
              <DismissButton id={o.id} projectId={projectId} actingActorId={actingActorId} />
            </div>
          </li>
        ))}
        {open.length === 0 && <li className="text-sm text-slate-400">Inbox is empty.</li>}
      </ul>

      {promote && (
        <PromoteModal
          observation={promote}
          projectId={projectId}
          actingActorId={actingActorId}
          onClose={() => setPromote(null)}
        />
      )}
    </Card>
  )
}

function DismissButton({ id, projectId, actingActorId }: { id: string; projectId: string; actingActorId: string }) {
  const invalidate = useProjectInvalidator(projectId)
  const dismiss = useMutation({
    mutationFn: () => api.dismissObservation(id, actingActorId, 'noise'),
    onSuccess: () => invalidate(),
  })
  return <Button variant="subtle" disabled={dismiss.isPending} onClick={() => dismiss.mutate()}>Dismiss</Button>
}

function PromoteModal({
  observation, projectId, actingActorId, onClose,
}: { observation: Observation; projectId: string; actingActorId: string; onClose: () => void }) {
  const invalidate = useProjectInvalidator(projectId)
  const [title, setTitle] = useState('')
  const promote = useMutation({
    mutationFn: () => api.promoteObservation(observation.id, { actorId: actingActorId, title, description: observation.payload }),
    onSuccess: () => {
      invalidate()
      onClose()
    },
  })
  return (
    <Modal title="Promote to change request" onClose={onClose}>
      <div className="space-y-3">
        <p className="rounded bg-slate-50 p-2 font-mono text-xs text-slate-600">{observation.payload}</p>
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

/** Demo helper: post a signal to a source as the SDK/Seq would, so capture works before the real SDK. */
function SimulateCapture({ projectId }: { projectId: string }) {
  const { data: sources } = useSources(projectId)
  const invalidate = useProjectInvalidator(projectId)
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
    onSuccess: () => {
      invalidate()
      setMessage('')
    },
  })

  if (!sources || sources.length === 0) {
    return <p className="text-xs text-slate-400">Add an observation source in Manage to simulate capture.</p>
  }

  return (
    <div className="space-y-2 rounded-md border border-dashed border-slate-300 p-2.5">
      <p className="text-xs font-medium text-slate-500">Simulate capture</p>
      <div className="flex gap-2">
        <Select value={effectiveSource} onChange={(e) => setSourceId(e.target.value)} className="max-w-32">
          {sources.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}
        </Select>
        <TextInput value={message} onChange={(e) => setMessage(e.target.value)} placeholder="signal message" />
      </div>
      <div className="flex gap-2">
        <TextInput value={fingerprint} onChange={(e) => setFingerprint(e.target.value)} placeholder="fingerprint (optional, for aggregation)" />
        <Button variant="default" disabled={!message.trim() || ingest.isPending} onClick={() => ingest.mutate()}>Send</Button>
      </div>
      <ErrorText error={ingest.error} />
    </div>
  )
}
