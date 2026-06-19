import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { api } from '../api/client'
import { useProjectInvalidator } from '../api/hooks'
import type { ChangeKind, Priority } from '../api/types'
import { Button, ErrorText, Field, Modal, Select, TextInput, Textarea } from '../components/ui'

export function CreateChangeModal({
  projectId, onClose,
}: { projectId: string; onClose: () => void }) {
  const invalidate = useProjectInvalidator(projectId)
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [kind, setKind] = useState<ChangeKind>('Feature')
  const [priority, setPriority] = useState<Priority>('None')

  const create = useMutation({
    mutationFn: () =>
      api.createChange(projectId, { title, description: description || null, kind, priority }),
    onSuccess: () => {
      invalidate()
      onClose()
    },
  })

  return (
    <Modal title="New change request" onClose={onClose}>
      <div className="space-y-3">
        <Field label="Title">
          <TextInput value={title} onChange={(e) => setTitle(e.target.value)} placeholder="Add CSV export" autoFocus />
        </Field>
        <Field label="Description">
          <Textarea value={description} onChange={(e) => setDescription(e.target.value)} />
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
        <div className="flex justify-end gap-2 pt-1">
          <Button variant="subtle" onClick={onClose}>Cancel</Button>
          <Button variant="primary" disabled={!title.trim() || create.isPending} onClick={() => create.mutate()}>
            Create
          </Button>
        </div>
      </div>
    </Modal>
  )
}
