import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router-dom'
import type { Member } from '../api/types'
import { useChanges, useInbox, useLatestWorkOrder, useMyAssistConversations, useProjects } from '../api/hooks'
import { useBoardLiveUpdates } from '../api/realtime'
import { api } from '../api/client'
import { relativeTime } from '../lifecycle'
import { Badge, Button, Modal, TextInput, cx } from '../components/ui'
import { OverflowMenu } from '../components/OverflowMenu'
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
  const [confirmingExport, setConfirmingExport] = useState(false)
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

  // The last work order and how much of it has come back, for the tracking strip + re-export warning.
  const { data: workOrder } = useLatestWorkOrder(projectId)
  const outstanding = workOrder?.pendingChangeIds.length ?? 0

  const exportInDev = useMutation({
    mutationFn: () => api.exportInDevelopment(projectId),
    onSuccess: () => toast.success(`Exported ${inDevCount} in-development change${inDevCount === 1 ? '' : 's'}`),
    onError: (e) => toast.error(e instanceof Error ? e.message : 'Export failed'),
  })

  // Warn before re-exporting changes that are still out with an agent — but don't block it: the old work
  // order's token stays live on purpose, so both can still report.
  const startExport = () => {
    if (outstanding > 0) setConfirmingExport(true)
    else exportInDev.mutate()
  }

  // Secondary toolbar actions, single-sourced so the desktop buttons and the mobile ⋯ menu can't drift.
  type ToolbarAction = { label: string; title?: string; disabled?: boolean; badge?: number; onClick: () => void }
  const secondaryActions: ToolbarAction[] = [{
    label: 'Export',
    title: inDevCount === 0 ? 'No in-development changes to export' : 'Download a Claude Code work order',
    disabled: inDevCount === 0 || exportInDev.isPending,
    onClick: startExport,
  }]
  if (!humanOnly) {
    secondaryActions.push({
      label: 'Capture Assist',
      title: myTurnCount > 0 ? `${myTurnCount} conversation${myTurnCount === 1 ? '' : 's'} awaiting your reply` : undefined,
      badge: myTurnCount > 0 ? myTurnCount : undefined,
      onClick: () => navigate(`/projects/${projectId}/capture`),
    })
  }

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

          {/* Secondary actions: inline buttons on desktop, folded into a ⋯ overflow menu on phones. */}
          <div className="hidden items-center gap-2 sm:flex">
            {secondaryActions.map((a, i) => (
              <Button key={i} variant="default" disabled={a.disabled} title={a.title} onClick={a.onClick}>
                {a.label}{a.badge ? <span className="ml-1.5"><Badge tone="indigo">{a.badge}</Badge></span> : null}
              </Button>
            ))}
          </div>
          <div className="sm:hidden">
            <OverflowMenu
              label="More actions"
              align="left"
              actions={secondaryActions.map((a) => ({
                label: a.label,
                title: a.title,
                disabled: a.disabled,
                onClick: a.onClick,
                trailing: a.badge ? <Badge tone="indigo">{a.badge}</Badge> : undefined,
              }))}
            />
          </div>
          <Button variant="primary" onClick={() => setCreating(true)}>+ New<span className="hidden sm:inline"> change</span></Button>
        </div>
      </div>

      {/* The export stops being fire-and-forget: how much of the last work order has come back. Status,
          not an action — so it sits under the header rather than in the toolbar, which folds into an
          overflow menu on phones. */}
      {workOrder && workOrder.itemCount > 0 && (
        <p className="-mt-2 text-[11px] text-slate-400">
          Exported {relativeTime(workOrder.exportedAt)} ·{' '}
          {workOrder.reportedCount + workOrder.blockedCount}/{workOrder.itemCount} reported back
          {workOrder.blockedCount > 0 && (
            <>
              {' · '}
              <span className="text-amber-600 dark:text-amber-400">{workOrder.blockedCount} blocked</span>
            </>
          )}
        </p>
      )}

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
      {confirmingExport && workOrder && (
        <Modal title="Some changes are still out" onClose={() => setConfirmingExport(false)}>
          <div className="space-y-3">
            <div className="rounded-md border border-amber-200 bg-amber-50/40 p-3 text-sm text-amber-900 dark:border-amber-900/60 dark:bg-amber-950/20 dark:text-amber-200">
              <p className="font-medium">
                {outstanding} change{outstanding === 1 ? '' : 's'} {outstanding === 1 ? 'is' : 'are'} still
                out with an agent.
              </p>
              <p className="mt-1">
                They were exported {relativeTime(workOrder.exportedAt)} and haven't reported back.
                Re-exporting hands them out a second time — both work orders stay valid, so either can
                still report.
              </p>
            </div>
            <div className="flex justify-end gap-2">
              <Button variant="default" onClick={() => setConfirmingExport(false)}>Cancel</Button>
              <Button
                variant="primary"
                disabled={exportInDev.isPending}
                onClick={() => { setConfirmingExport(false); exportInDev.mutate() }}
              >
                Export anyway
              </Button>
            </div>
          </div>
        </Modal>
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
