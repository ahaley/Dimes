import { useState } from 'react'
import type { Member } from '../api/types'
import { Inbox } from './Inbox'
import { ChangeBoard } from './ChangeBoard'
import { ChangeDetail } from './ChangeDetail'

export function Workspace({
  projectId, actingActorId, members,
}: { projectId: string; actingActorId: string; members: Member[] }) {
  const [selectedChangeId, setSelectedChangeId] = useState<string>()

  return (
    <div className="grid gap-6 lg:grid-cols-[320px_1fr]">
      <Inbox projectId={projectId} actingActorId={actingActorId} />
      <ChangeBoard projectId={projectId} actingActorId={actingActorId} onSelect={setSelectedChangeId} />

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
