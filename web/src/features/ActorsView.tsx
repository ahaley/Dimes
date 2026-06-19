import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { useActors } from '../api/hooks'
import type { ActorSummary } from '../api/types'
import { Badge, Button, Card } from '../components/ui'
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
  const invalidate = () => qc.invalidateQueries({ queryKey: ['actors'] })
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
