import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router-dom'
import type { Member } from '../api/types'
import { useChanges, useInbox } from '../api/hooks'
import { useBoardLiveUpdates } from '../api/realtime'
import { api } from '../api/client'
import { Badge, Button } from '../components/ui'
import { useToast } from '../components/Toast'
import { ChangeBoard } from './ChangeBoard'
import { ChangeDetail } from './ChangeDetail'
import { InboxDrawer } from './InboxDrawer'
import { CreateChangeModal } from './CreateChangeModal'

export function Workspace({
  actingActorId, members,
}: { actingActorId: string; members: Member[] }) {
  const { projectId = '', changeId } = useParams()
  const navigate = useNavigate()
  const [inboxOpen, setInboxOpen] = useState(false)
  const [creating, setCreating] = useState(false)
  const toast = useToast()

  // Live board updates from other users (create / edit / move / promote).
  useBoardLiveUpdates(projectId)

  const { data: observations } = useInbox(projectId)
  const inboxCount = (observations ?? []).filter((o) => o.status === 'New' || o.status === 'Clustered').length

  const { data: changes } = useChanges(projectId)
  const inDevCount = (changes ?? []).filter((c) => c.status === 'InDevelopment').length

  const exportInDev = useMutation({
    mutationFn: () => api.exportInDevelopment(projectId),
    onSuccess: () => toast.success(`Exported ${inDevCount} in-development change${inDevCount === 1 ? '' : 's'}`),
    onError: (e) => toast.error(e instanceof Error ? e.message : 'Export failed'),
  })

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-sm font-semibold uppercase tracking-wide text-slate-500">Change board</h1>
        <div className="flex items-center gap-2">
          <Button variant="default" onClick={() => setInboxOpen(true)}>
            Inbox{inboxCount > 0 && <span className="ml-1.5"><Badge tone="amber">{inboxCount}</Badge></span>}
          </Button>
          <Button
            variant="default"
            disabled={inDevCount === 0 || exportInDev.isPending}
            title={inDevCount === 0 ? 'No in-development changes to export' : 'Download a Claude Code work order'}
            onClick={() => exportInDev.mutate()}
          >
            Export
          </Button>
          <Button variant="primary" onClick={() => setCreating(true)}>+ New change</Button>
        </div>
      </div>

      <ChangeBoard
        projectId={projectId}
        members={members}
        onSelect={(id) => navigate(`/projects/${projectId}/changes/${id}`)}
      />

      {inboxOpen && (
        <InboxDrawer projectId={projectId} onClose={() => setInboxOpen(false)} />
      )}
      {creating && (
        <CreateChangeModal projectId={projectId} onClose={() => setCreating(false)} />
      )}
      {changeId && (
        <ChangeDetail
          changeId={changeId}
          projectId={projectId}
          actingActorId={actingActorId}
          members={members}
          onClose={() => navigate(`/projects/${projectId}`)}
        />
      )}
    </div>
  )
}
