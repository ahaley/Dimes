import { useState } from 'react'
import { useCreateProject, useProjectQuota } from '../api/hooks'
import type { Project } from '../api/types'
import { Button, ErrorText, Field, Modal, TextInput } from '../components/ui'

const KEY_RE = /^[A-Z][A-Z0-9]{1,5}$/

// Suggest a key from the project name: uppercase alphanumerics, leading letter, capped at 6.
function suggestKey(name: string): string {
  let s = name.toUpperCase().replace(/[^A-Z0-9]/g, '')
  if (s && !/[A-Z]/.test(s[0])) s = 'P' + s
  return s.slice(0, 6)
}

export function CreateProjectModal({ onClose, onCreated }: { onClose: () => void; onCreated: (p: Project) => void }) {
  const { data: quota } = useProjectQuota()
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [key, setKey] = useState('')
  // Until the user edits the key directly, keep it derived from the name.
  const [keyEdited, setKeyEdited] = useState(false)

  const onNameChange = (value: string) => {
    setName(value)
    if (!keyEdited) setKey(suggestKey(value))
  }

  const create = useCreateProject()
  const submit = () =>
    create.mutate({ name, description: description || null, key }, { onSuccess: onCreated })

  const keyValid = KEY_RE.test(key)

  return (
    <Modal title="New project" onClose={onClose}>
      <div className="space-y-3">
        <Field label="Name">
          <TextInput value={name} onChange={(e) => onNameChange(e.target.value)} placeholder="Acme Web" autoFocus />
        </Field>
        <Field label="Key">
          <TextInput
            value={key}
            onChange={(e) => { setKeyEdited(true); setKey(e.target.value.toUpperCase()) }}
            placeholder="ACME"
            maxLength={6}
          />
          <span className="mt-1 block text-xs text-slate-400">
            2–6 letters/digits, starts with a letter. Prefixes change ids (e.g. <span className="font-mono">{(keyValid ? key : 'ACME')}-142</span>). Can’t be changed later.
          </span>
        </Field>
        <Field label="Description">
          <TextInput value={description} onChange={(e) => setDescription(e.target.value)} placeholder="optional" />
        </Field>
        <ErrorText error={create.error} />
        <div className="flex items-center justify-between gap-2 pt-1">
          {/* Non-admins create against a per-user allowance; archiving a project doesn't give a slot back. */}
          <span className="text-xs text-slate-400">
            {quota && !quota.unlimited
              ? `${quota.used} of ${quota.limit} projects used`
              : ''}
          </span>
          <div className="flex gap-2">
            <Button variant="subtle" onClick={onClose}>Cancel</Button>
            <Button variant="primary" disabled={!name.trim() || !keyValid || create.isPending} onClick={submit}>
              Create
            </Button>
          </div>
        </div>
      </div>
    </Modal>
  )
}
