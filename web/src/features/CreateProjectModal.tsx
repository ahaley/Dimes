import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { keys } from '../api/hooks'
import type { Project } from '../api/types'
import { Button, ErrorText, Field, Modal, TextInput } from '../components/ui'

export function CreateProjectModal({ onClose, onCreated }: { onClose: () => void; onCreated: (p: Project) => void }) {
  const qc = useQueryClient()
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')

  const create = useMutation({
    mutationFn: () => api.createProject({ name, description: description || null }),
    onSuccess: (p) => {
      qc.invalidateQueries({ queryKey: keys.projects })
      onCreated(p)
    },
  })

  return (
    <Modal title="New project" onClose={onClose}>
      <div className="space-y-3">
        <Field label="Name">
          <TextInput value={name} onChange={(e) => setName(e.target.value)} placeholder="Acme Web" autoFocus />
        </Field>
        <Field label="Description">
          <TextInput value={description} onChange={(e) => setDescription(e.target.value)} placeholder="optional" />
        </Field>
        <ErrorText error={create.error} />
        <div className="flex justify-end gap-2 pt-1">
          <Button variant="subtle" onClick={onClose}>Cancel</Button>
          <Button variant="primary" disabled={!name.trim() || create.isPending} onClick={() => create.mutate()}>
            Create
          </Button>
        </div>
      </div>
    </Modal>
  )
}
