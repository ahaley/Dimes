import { useState } from 'react'
import { useChanges } from '../api/hooks'
import { LIFECYCLE_COLUMNS, type ChangeRequest } from '../api/types'
import { STATUS_TONE } from '../lifecycle'
import { Badge, Button, Card } from '../components/ui'
import { CreateChangeModal } from './CreateChangeModal'

export function ChangeBoard({
  projectId, actingActorId, onSelect,
}: { projectId: string; actingActorId: string; onSelect: (id: string) => void }) {
  const { data: changes } = useChanges(projectId)
  const [creating, setCreating] = useState(false)

  const byStatus = (status: string) => (changes ?? []).filter((c) => c.status === status)
  const terminal = (changes ?? []).filter((c) => c.status === 'Rejected' || c.status === 'Duplicate')

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <h2 className="text-sm font-semibold text-slate-700">Change board</h2>
        <Button variant="primary" onClick={() => setCreating(true)}>+ New change</Button>
      </div>

      <div className="grid grid-cols-2 gap-3 lg:grid-cols-3 xl:grid-cols-6">
        {LIFECYCLE_COLUMNS.map((status) => (
          <div key={status} className="rounded-lg bg-slate-100/70 p-2">
            <div className="mb-2 flex items-center justify-between px-1">
              <span className="text-xs font-semibold text-slate-600">{status}</span>
              <Badge tone={STATUS_TONE[status]}>{byStatus(status).length}</Badge>
            </div>
            <div className="space-y-2">
              {byStatus(status).map((c) => <ChangeCard key={c.id} change={c} onClick={() => onSelect(c.id)} />)}
            </div>
          </div>
        ))}
      </div>

      {terminal.length > 0 && (
        <div className="flex flex-wrap items-center gap-2 pt-1">
          <span className="text-xs text-slate-400">Closed:</span>
          {terminal.map((c) => (
            <button key={c.id} onClick={() => onSelect(c.id)} className="text-xs text-slate-500 underline-offset-2 hover:underline">
              {c.title} <span className="text-slate-400">({c.status})</span>
            </button>
          ))}
        </div>
      )}

      {creating && (
        <CreateChangeModal projectId={projectId} actingActorId={actingActorId} onClose={() => setCreating(false)} />
      )}
    </div>
  )
}

function ChangeCard({ change, onClick }: { change: ChangeRequest; onClick: () => void }) {
  return (
    <Card className="cursor-pointer p-2.5 hover:border-indigo-300" >
      <button onClick={onClick} className="w-full text-left">
        <p className="text-sm font-medium text-slate-800">{change.title}</p>
        <div className="mt-1.5 flex items-center gap-1.5">
          <Badge tone="slate">{change.kind}</Badge>
          {change.priority !== 'None' && <Badge tone="amber">{change.priority}</Badge>}
        </div>
      </button>
    </Card>
  )
}
