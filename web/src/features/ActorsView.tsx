import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { useActors } from '../api/hooks'
import type { ActorSummary } from '../api/types'
import { Badge, Button, Card, ErrorText, Field, TextInput } from '../components/ui'
import { useToast } from '../components/Toast'

/** App-level management of created actors — agents by default, with guarded deletion of orphans. */
export function ActorsView() {
  const [agentsOnly, setAgentsOnly] = useState(true)
  const [includeArchived, setIncludeArchived] = useState(false)
  const { data: actors } = useActors(agentsOnly, includeArchived)

  return (
    <div className="mx-auto max-w-2xl space-y-4">
      <div className="flex items-start justify-between gap-4">
        <div>
          <h1 className="text-lg font-semibold text-slate-800 dark:text-slate-100">Actors</h1>
          <p className="mt-1 text-sm text-slate-500">
            Identities created via project membership. Removing a member keeps the actor (so its history
            stays valid); archive it to hide it from active lists, or delete it once it has no project and
            no references.
          </p>
        </div>
        <div className="flex shrink-0 flex-col items-end gap-1 pt-1 text-xs text-slate-600 dark:text-slate-300">
          <label className="flex items-center gap-2">
            <input type="checkbox" checked={agentsOnly} onChange={(e) => setAgentsOnly(e.target.checked)} />
            Agents only
          </label>
          <label className="flex items-center gap-2">
            <input type="checkbox" checked={includeArchived} onChange={(e) => setIncludeArchived(e.target.checked)} />
            Show archived
          </label>
        </div>
      </div>

      <Card className="divide-y divide-slate-100 dark:divide-slate-800">
        {(actors ?? []).map((a) => <ActorRow key={a.id} actor={a} />)}
        {actors?.length === 0 && <p className="p-4 text-sm text-slate-400">No actors to show.</p>}
      </Card>
    </div>
  )
}

function ActorRow({ actor }: { actor: ActorSummary }) {
  const qc = useQueryClient()
  const toast = useToast()
  const [editing, setEditing] = useState(false)
  const [displayName, setDisplayName] = useState(actor.displayName)
  const [email, setEmail] = useState(actor.email ?? '')
  const invalidate = () => qc.invalidateQueries({ queryKey: ['actors'] })
  const save = useMutation({
    mutationFn: () => api.updateActor(actor.id, { displayName, email: email || null }),
    onSuccess: () => { invalidate(); setEditing(false); toast.success(`Updated ${displayName}`) },
    onError: (e) => toast.error(e instanceof Error ? e.message : 'Could not update actor'),
  })
  const remove = useMutation({
    mutationFn: () => api.deleteActor(actor.id),
    onSuccess: () => { invalidate(); toast.success(`Deleted ${actor.displayName}`) },
    onError: (e) => toast.error(e instanceof Error ? e.message : 'Could not delete actor'),
  })
  const archive = useMutation({
    mutationFn: () => (actor.isArchived ? api.unarchiveActor(actor.id) : api.archiveActor(actor.id)),
    onSuccess: () => {
      invalidate()
      toast.success(`${actor.isArchived ? 'Unarchived' : 'Archived'} ${actor.displayName}`)
    },
    onError: (e) => toast.error(e instanceof Error ? e.message : 'Could not update actor'),
  })

  const membership = actor.projectCount === 0
    ? 'no project'
    : `${actor.projectCount} project${actor.projectCount === 1 ? '' : 's'}`
  const lockReason = actor.projectCount > 0
    ? `Locked — still a member of ${membership}; remove the membership first.`
    : 'Locked — has history (changes, comments, or audit) that is kept, so it can’t be deleted.'

  if (editing) {
    return (
      <div className="space-y-2 p-3">
        <Field label="Display name">
          <TextInput value={displayName} onChange={(e) => setDisplayName(e.target.value)} autoFocus />
        </Field>
        <Field label="Email">
          <TextInput value={email} onChange={(e) => setEmail(e.target.value)} placeholder="optional" />
        </Field>
        <ErrorText error={save.error} />
        <div className="flex justify-end gap-2">
          <Button
            variant="subtle"
            onClick={() => { setDisplayName(actor.displayName); setEmail(actor.email ?? ''); setEditing(false) }}
          >
            Cancel
          </Button>
          <Button variant="primary" disabled={!displayName.trim() || save.isPending} onClick={() => save.mutate()}>
            Save
          </Button>
        </div>
      </div>
    )
  }

  return (
    <div className="p-3 text-sm text-slate-700 dark:text-slate-200">
      <div className="flex items-center gap-2">
        <span className="font-medium text-slate-800 dark:text-slate-100">{actor.displayName}</span>
        <Badge tone={actor.type === 'Agent' ? 'violet' : 'slate'}>{actor.type}</Badge>
        <span className="text-slate-400">{actor.providerName ?? '—'}</span>
        <Badge tone={actor.projectCount === 0 ? 'amber' : 'slate'}>{membership}</Badge>
        {actor.isArchived && <Badge tone="amber">archived</Badge>}
        {!actor.deletable && <Badge tone="red">locked</Badge>}
        <span className="ml-auto flex items-center gap-1">
          <Button variant="subtle" onClick={() => setEditing(true)}>Edit</Button>
          <Button
            variant="subtle"
            disabled={archive.isPending}
            title={actor.isArchived ? 'Restore to active lists' : 'Hide from active lists'}
            onClick={() => archive.mutate()}
          >
            {actor.isArchived ? 'Unarchive' : 'Archive'}
          </Button>
          <Button
            variant="subtle"
            disabled={!actor.deletable || remove.isPending}
            title={actor.deletable ? 'Delete actor' : lockReason}
            onClick={() => { if (window.confirm(`Delete actor "${actor.displayName}"?`)) remove.mutate() }}
          >
            Delete
          </Button>
        </span>
      </div>
      {!actor.deletable && <p className="mt-1 text-xs text-slate-500">{lockReason}</p>}
    </div>
  )
}
