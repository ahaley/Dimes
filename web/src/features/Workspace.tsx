import { useState } from 'react'
import type { Member } from '../api/types'
import { useInbox } from '../api/hooks'
import { Badge, Button } from '../components/ui'
import { ChangeBoard } from './ChangeBoard'
import { ChangeDetail } from './ChangeDetail'
import { InboxDrawer } from './InboxDrawer'
import { CreateChangeModal } from './CreateChangeModal'

export function Workspace({
  projectId, actingActorId, members,
}: { projectId: string; actingActorId: string; members: Member[] }) {
  const [selectedChangeId, setSelectedChangeId] = useState<string>()
  const [inboxOpen, setInboxOpen] = useState(false)
  const [creating, setCreating] = useState(false)

  const { data: observations } = useInbox(projectId)
  const inboxCount = (observations ?? []).filter((o) => o.status === 'New' || o.status === 'Clustered').length

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-sm font-semibold uppercase tracking-wide text-slate-500">Change board</h1>
        <div className="flex items-center gap-2">
          <Button variant="default" onClick={() => setInboxOpen(true)}>
            Inbox{inboxCount > 0 && <span className="ml-1.5"><Badge tone="amber">{inboxCount}</Badge></span>}
          </Button>
          <Button variant="primary" onClick={() => setCreating(true)}>+ New change</Button>
        </div>
      </div>

      <ChangeBoard
        projectId={projectId}
        actingActorId={actingActorId}
        members={members}
        onSelect={setSelectedChangeId}
      />

      {inboxOpen && (
        <InboxDrawer projectId={projectId} actingActorId={actingActorId} onClose={() => setInboxOpen(false)} />
      )}
      {creating && (
        <CreateChangeModal projectId={projectId} actingActorId={actingActorId} onClose={() => setCreating(false)} />
      )}
      {selectedChangeId && (
        <ChangeDetail
          changeId={selectedChangeId}
          projectId={projectId}
          actingActorId={actingActorId}
          members={members}
          onClose={() => setSelectedChangeId(undefined)}
        />
      )}
    </div>
  )
}
