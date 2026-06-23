import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router-dom'
import type { Member } from '../api/types'
import { useChanges, useInbox, useMyAssistConversations, useProjects } from '../api/hooks'
import { useBoardLiveUpdates } from '../api/realtime'
import { api } from '../api/client'
import { Badge, Button, TextInput } from '../components/ui'
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
  const [query, setQuery] = useState('')
  const toast = useToast()

  // Live board updates from other users (create / edit / move / promote).
  useBoardLiveUpdates(projectId)

  // Human-only projects hide all AI-agent affordances (Capture Assist entry, etc.).
  const { data: projects } = useProjects(true, true)
  const humanOnly = projects?.find((p) => p.id === projectId)?.humanOnly ?? false

  const { data: observations } = useInbox(projectId)
  // Mirror the drawer: latent signals count for everyone; directed ones only for their target.
  const inboxCount = (observations ?? []).filter(
    (o) => (o.status === 'New' || o.status === 'Clustered') && (!o.targetActorId || o.targetActorId === actingActorId),
  ).length

  const { data: changes } = useChanges(projectId)
  const inDevCount = (changes ?? []).filter((c) => c.status === 'InDevelopment').length

  // Capture Assist conversations I started where the teammate has replied and it's my turn to read/respond.
  const { data: myConversations } = useMyAssistConversations(projectId)
  const myTurnCount = (myConversations ?? []).filter((c) => c.status === 'AwaitingRequester').length

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
          <div className="w-40 sm:w-56">
            <TextInput
              type="search"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Search changes…"
              aria-label="Search changes"
            />
          </div>
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
          {!humanOnly && (
            <Button
              variant="default"
              onClick={() => navigate(`/projects/${projectId}/capture`)}
              title={myTurnCount > 0 ? `${myTurnCount} conversation${myTurnCount === 1 ? '' : 's'} awaiting your reply` : undefined}
            >
              Capture Assist{myTurnCount > 0 && <span className="ml-1.5"><Badge tone="indigo">{myTurnCount}</Badge></span>}
            </Button>
          )}
          <Button variant="primary" onClick={() => setCreating(true)}>+ New change</Button>
        </div>
      </div>

      <ChangeBoard
        projectId={projectId}
        members={members}
        query={query}
        onSelect={(id) => navigate(`/projects/${projectId}/changes/${id}`)}
      />

      {inboxOpen && (
        <InboxDrawer projectId={projectId} onClose={() => setInboxOpen(false)} />
      )}
      {creating && (
        <CreateChangeModal
          projectId={projectId}
          members={members}
          actingActorId={actingActorId}
          onClose={() => setCreating(false)}
        />
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
