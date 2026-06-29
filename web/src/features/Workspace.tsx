import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router-dom'
import type { Member } from '../api/types'
import { useChanges, useInbox, useMyAssistConversations, useProjects } from '../api/hooks'
import { useBoardLiveUpdates } from '../api/realtime'
import { api } from '../api/client'
import { Badge, Button, TextInput, cx } from '../components/ui'
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
  const [moreOpen, setMoreOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [mineOnly, setMineOnly] = useState(false)
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
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <h1 className="hidden text-sm font-semibold uppercase tracking-wide text-slate-500 sm:block">Change board</h1>
        <div className="flex flex-wrap items-center gap-2">
          <div className="relative w-full sm:w-60">
            <TextInput
              type="search"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              placeholder="Search changes…"
              aria-label="Search changes"
              className="pr-8"
            />
            <button
              type="button"
              onClick={() => setMineOnly((v) => !v)}
              aria-pressed={mineOnly}
              aria-label="Filter to requests assigned to me"
              title={mineOnly ? 'Showing requests assigned to you' : 'Show only requests assigned to you'}
              className={cx(
                'absolute right-1 top-1/2 flex h-6 w-6 -translate-y-1/2 items-center justify-center rounded transition-colors',
                mineOnly
                  ? 'bg-indigo-600 text-white'
                  : 'text-slate-400 hover:bg-slate-100 hover:text-slate-600 dark:hover:bg-slate-700 dark:hover:text-slate-200',
              )}
            >
              <svg viewBox="0 0 16 16" className="h-3.5 w-3.5" fill="currentColor" aria-hidden="true">
                <path d="M8 8a3 3 0 1 0 0-6 3 3 0 0 0 0 6Zm0 1.4c-2.76 0-5 1.46-5 3.26V14h10v-1.34c0-1.8-2.24-3.26-5-3.26Z" />
              </svg>
            </button>
          </div>
          <Button variant="default" onClick={() => setInboxOpen(true)}>
            Inbox{inboxCount > 0 && <span className="ml-1.5"><Badge tone="amber">{inboxCount}</Badge></span>}
          </Button>

          {/* Secondary actions: inline on desktop, folded into a ⋯ overflow menu on phones. */}
          <div className="hidden items-center gap-2 sm:flex">
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
          </div>
          <div className="relative sm:hidden">
            <Button variant="default" aria-label="More actions" aria-haspopup="menu" onClick={() => setMoreOpen((v) => !v)}>⋯</Button>
            {moreOpen && (
              <>
                <div className="fixed inset-0 z-10" onClick={() => setMoreOpen(false)} />
                <div className="absolute left-0 top-full z-20 mt-1 w-48 max-w-[calc(100vw-2rem)] overflow-hidden rounded-md border border-slate-200 bg-white py-1 shadow-lg dark:border-slate-700 dark:bg-slate-800">
                  <button
                    disabled={inDevCount === 0 || exportInDev.isPending}
                    onClick={() => { setMoreOpen(false); exportInDev.mutate() }}
                    className="block w-full px-3 py-2.5 text-left text-sm text-slate-700 hover:bg-slate-50 disabled:opacity-50 dark:text-slate-200 dark:hover:bg-slate-700"
                  >
                    Export work order
                  </button>
                  {!humanOnly && (
                    <button
                      onClick={() => { setMoreOpen(false); navigate(`/projects/${projectId}/capture`) }}
                      className="flex w-full items-center justify-between gap-2 px-3 py-2.5 text-left text-sm text-slate-700 hover:bg-slate-50 dark:text-slate-200 dark:hover:bg-slate-700"
                    >
                      <span>Capture Assist</span>
                      {myTurnCount > 0 && <Badge tone="indigo">{myTurnCount}</Badge>}
                    </button>
                  )}
                </div>
              </>
            )}
          </div>
          <Button variant="primary" onClick={() => setCreating(true)}>+ New<span className="hidden sm:inline"> change</span></Button>
        </div>
      </div>

      <ChangeBoard
        projectId={projectId}
        members={members}
        query={query}
        mineOnly={mineOnly}
        actingActorId={actingActorId}
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
