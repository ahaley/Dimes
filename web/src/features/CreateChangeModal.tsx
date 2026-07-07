import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { api } from '../api/client'
import { useProjectInvalidator } from '../api/hooks'
import type { ChangeKind, Member, Priority } from '../api/types'
import { Button, ErrorText, Field, Modal, Select, TextInput, Textarea } from '../components/ui'

export function CreateChangeModal({
  projectId, members, actingActorId, onClose,
}: { projectId: string; members: Member[]; actingActorId: string; onClose: () => void }) {
  const invalidate = useProjectInvalidator(projectId)
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [kind, setKind] = useState<ChangeKind>('Feature')
  const [priority, setPriority] = useState<Priority>('None')
  const [recipient, setRecipient] = useState('')

  // Setting a recipient is a Contributor+ action (matches the inline control on the change detail).
  const myRole = members.find((m) => m.actorId === actingActorId)?.role
  const canAssign = myRole === 'Contributor' || myRole === 'Maintainer'

  const create = useMutation({
    mutationFn: () =>
      api.createChange(projectId, { title, description: description || null, kind, priority, assigneeActorId: recipient || null }),
    onSuccess: () => {
      invalidate()
      onClose()
    },
  })

  // Guard against losing typed work: confirm before closing if the title or description has text. The
  // backdrop and ✕ both call the Modal's onClose, so routing them through here covers every close path
  // except a successful create (which calls onClose directly, with no prompt).
  const attemptClose = () => {
    if ((title.trim() || description.trim()) && !window.confirm('Discard this change request? Your unsaved text will be lost.')) {
      return
    }
    onClose()
  }

  return (
    <Modal title="New change request" onClose={attemptClose}>
      <div className="space-y-3">
        <Field label="Title">
          <TextInput value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Add CSV export" autoFocus />
        </Field>
        <Field label="Description">
          <Textarea value={description} onChange={(e) => setDescription(e.target.value)} />
        </Field>
        <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
          <Field label="Kind">
            {/* Observation-driven is omitted on purpose: that kind is applied only when promoting an
                observation, never chosen manually (the API rejects it on create). */}
            <Select value={kind} onChange={(e) => setKind(e.target.value as ChangeKind)}>
              <option value="Feature">Feature</option>
              <option value="Problem">Problem</option>
              <option value="Epic">Epic</option>
              <option value="Chore">Chore</option>
            </Select>
          </Field>
          <Field label="Priority">
            <Select value={priority} onChange={(e) => setPriority(e.target.value as Priority)}>
              {['None', 'Low', 'Medium', 'High', 'Critical'].map((p) => <option key={p} value={p}>{p}</option>)}
            </Select>
          </Field>
        </div>
        {canAssign && (
          <Field label="Recipient">
            <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
              <Select value={recipient} onChange={(e) => setRecipient(e.target.value)} className="flex-1">
                <option value="">Unassigned</option>
                {members.map((m) => <option key={m.actorId} value={m.actorId}>{m.displayName}</option>)}
              </Select>
              <Button variant="subtle" disabled={recipient === actingActorId} onClick={() => setRecipient(actingActorId)}>
                Assign to me
              </Button>
            </div>
          </Field>
        )}
        <ErrorText error={create.error} />
        <div className="flex justify-end gap-2 pt-1">
          <Button variant="subtle" onClick={attemptClose}>Cancel</Button>
          <Button variant="primary" disabled={!title.trim() || create.isPending} onClick={() => create.mutate()}>
            Create
          </Button>
        </div>
      </div>
    </Modal>
  )
}
