import { useNavigate, useParams } from 'react-router-dom'
import { useActor } from '../api/hooks'
import type { MemberRole } from '../api/types'
import { Badge, Button, Card } from '../components/ui'

// Highest-authority-first ordering for a tidy role summary.
const ROLE_ORDER: MemberRole[] = ['Maintainer', 'Contributor', 'Reporter', 'Assistant']

/** Actor presentation: identity, LLM provider binding, and the actor's project role(s) in one place. */
export function ActorDetailView() {
  const { actorId } = useParams()
  const navigate = useNavigate()
  const { data: actor, isLoading, isError } = useActor(actorId)

  if (isLoading) return <p className="text-sm text-slate-400">Loading…</p>
  if (isError || !actor) {
    return (
      <div className="mx-auto max-w-2xl space-y-4">
        <Button variant="subtle" onClick={() => navigate('/actors')}>← Actors</Button>
        <Card className="p-6 text-sm text-slate-500">Actor not found.</Card>
      </div>
    )
  }

  const roles = ROLE_ORDER.filter((r) => actor.memberships.some((m) => m.role === r))

  return (
    <div className="mx-auto max-w-2xl space-y-4">
      <Button variant="subtle" onClick={() => navigate('/actors')}>← Actors</Button>

      <Card className="space-y-4 p-5">
        <div className="flex items-center gap-2">
          <h1 className="text-lg font-semibold text-slate-800 dark:text-slate-100">{actor.displayName}</h1>
          <Badge tone={actor.type === 'Agent' ? 'violet' : 'slate'}>{actor.type}</Badge>
          {actor.isArchived && <Badge tone="amber">archived</Badge>}
        </div>

        <dl className="grid grid-cols-[8rem_1fr] gap-x-3 gap-y-2 text-sm">
          <dt className="text-slate-400">Email</dt>
          <dd className="text-slate-700 dark:text-slate-200">{actor.email ?? '—'}</dd>

          {actor.type === 'Agent' && (
            <>
              <dt className="text-slate-400">LLM provider</dt>
              <dd className="text-slate-700 dark:text-slate-200">{actor.providerName ?? 'none configured'}</dd>
            </>
          )}

          <dt className="text-slate-400">Role{roles.length === 1 ? '' : 's'}</dt>
          <dd className="flex flex-wrap items-center gap-1">
            {roles.length === 0 ? (
              <span className="text-slate-400">No project roles yet</span>
            ) : (
              roles.map((r) => (
                <Badge key={r} tone={r === 'Maintainer' ? 'indigo' : 'slate'}>{r}</Badge>
              ))
            )}
          </dd>
        </dl>
      </Card>
    </div>
  )
}
