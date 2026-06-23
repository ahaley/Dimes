import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { api } from '../api/client'
import { useAudit, useChangeDetail, useProjectInvalidator, useProjects, useTransition } from '../api/hooks'
import type { ChangeStatus, Member, Priority } from '../api/types'
import { ALLOWED_TRANSITIONS, STATUS_TONE } from '../lifecycle'
import { Badge, Button, ErrorText, Field, Modal, Select, TextInput, Textarea } from '../components/ui'
import { useToast } from '../components/Toast'

const PRIORITIES: Priority[] = ['None', 'Low', 'Medium', 'High', 'Critical']

export function ChangeDetail({
  changeId, projectId, actingActorId, members, onClose,
}: { changeId: string; projectId: string; actingActorId: string; members: Member[]; onClose: () => void }) {
  const { data: detail } = useChangeDetail(changeId)
  const title = detail
    ? `${detail.change.displayKey ? `${detail.change.displayKey} · ` : ''}${detail.change.title}`
    : 'Change'
  return (
    <Modal title={title} onClose={onClose} wide>
      <ChangeDetailBody changeId={changeId} projectId={projectId} actingActorId={actingActorId} members={members} />
    </Modal>
  )
}

/** The change detail content, without the surrounding Modal — reused by the board modal and by
 * Focus Mode's detail pane. */
export function ChangeDetailBody({
  changeId, projectId, actingActorId, members,
}: { changeId: string; projectId: string; actingActorId: string; members: Member[] }) {
  const { data: detail, isLoading } = useChangeDetail(changeId)
  const { data: audit } = useAudit(changeId)
  const { data: projects } = useProjects(true, true)
  const project = projects?.find((p) => p.id === projectId)
  // Source control is a per-project toggle; default to shown if the project hasn't loaded yet.
  const scmEnabled = project?.sourceControlEnabled ?? true
  // Human-only projects hide AI-agent affordances (the "Ask agent" commentary control).
  const humanOnly = project?.humanOnly ?? false
  const invalidate = useProjectInvalidator(projectId)
  const transition = useTransition(projectId)

  const toast = useToast()
  const agents = members.filter((m) => m.type === 'Agent')
  const [commentBody, setCommentBody] = useState('')
  const [agentId, setAgentId] = useState('')
  const [scmUrl, setScmUrl] = useState('')

  const canEdit = !!detail && (
    detail.change.createdByActorId === actingActorId ||
    members.find((m) => m.actorId === actingActorId)?.role === 'Maintainer'
  )
  const [editing, setEditing] = useState(false)
  const [eTitle, setETitle] = useState('')
  const [eDesc, setEDesc] = useState('')
  const [ePriority, setEPriority] = useState<Priority>('None')
  const [eRecipient, setERecipient] = useState('')

  const startEdit = () => {
    if (!detail) return
    setETitle(detail.change.title)
    setEDesc(detail.change.description ?? '')
    setEPriority(detail.change.priority)
    setERecipient(detail.change.assigneeActorId ?? '')
    setEditing(true)
  }
  const saveDetails = useMutation({
    mutationFn: () =>
      api.updateChangeDetails(changeId, {
        title: eTitle,
        description: eDesc || null,
        priority: ePriority,
        assigneeActorId: eRecipient || null,
      }),
    onSuccess: () => { invalidate(changeId); setEditing(false); toast.success('Change updated') },
    onError: (e) => toast.error(e instanceof Error ? e.message : 'Could not save changes'),
  })

  const addComment = useMutation({
    mutationFn: () => api.addComment(changeId, { body: commentBody }),
    onSuccess: () => { invalidate(changeId); setCommentBody('') },
  })
  const askAgent = useMutation({
    mutationFn: () => api.agentComment(changeId, agentId || agents[0]?.actorId),
    onSuccess: () => invalidate(changeId),
  })
  const addScm = useMutation({
    mutationFn: () => api.addScmLink(changeId, { url: scmUrl }),
    onSuccess: () => { invalidate(changeId); setScmUrl('') },
  })

  return (
    <>
      {isLoading || !detail ? (
        <p className="text-sm text-slate-400">Loading…</p>
      ) : (
        <div className="space-y-5">
          {/* Status + transitions */}
          <div className="flex flex-wrap items-center gap-2">
            {detail.change.displayKey && (
              <span className="font-mono text-xs text-slate-400">{detail.change.displayKey}</span>
            )}
            <Badge tone={STATUS_TONE[detail.change.status]}>{detail.change.status}</Badge>
            <Badge tone="slate">{detail.change.kind}</Badge>
            {detail.change.priority !== 'None' && <Badge tone="amber">{detail.change.priority}</Badge>}
            <span className="ml-auto" />
            {canEdit && !editing && <Button variant="subtle" onClick={startEdit}>Edit</Button>}
            {ALLOWED_TRANSITIONS[detail.change.status]
              .filter((t) => t !== 'Duplicate')
              .map((target) => (
                <Button
                  key={target}
                  variant={target === 'Rejected' ? 'danger' : 'default'}
                  disabled={transition.isPending}
                  onClick={() => transition.mutate({ id: changeId, target: target as ChangeStatus })}
                >
                  → {target}
                </Button>
              ))}
          </div>
          <ErrorText error={transition.error} />

          {editing ? (
            <div className="space-y-2 rounded-md border border-slate-200 p-3 dark:border-slate-700">
              <Field label="Title">
                <TextInput value={eTitle} onChange={(e) => setETitle(e.target.value)} autoFocus />
              </Field>
              <Field label="Description">
                <Textarea value={eDesc} onChange={(e) => setEDesc(e.target.value)} />
              </Field>
              <Field label="Priority">
                <Select value={ePriority} onChange={(e) => setEPriority(e.target.value as Priority)}>
                  {PRIORITIES.map((p) => <option key={p} value={p}>{p}</option>)}
                </Select>
              </Field>
              <Field label="Recipient">
                <Select value={eRecipient} onChange={(e) => setERecipient(e.target.value)}>
                  <option value="">Unassigned</option>
                  {members.map((m) => <option key={m.actorId} value={m.actorId}>{m.displayName}</option>)}
                </Select>
              </Field>
              <ErrorText error={saveDetails.error} />
              <div className="flex justify-end gap-2">
                <Button variant="subtle" onClick={() => setEditing(false)}>Cancel</Button>
                <Button variant="primary" disabled={!eTitle.trim() || saveDetails.isPending} onClick={() => saveDetails.mutate()}>
                  Save
                </Button>
              </div>
            </div>
          ) : detail.change.description ? (
            <p className="text-sm text-slate-600 dark:text-slate-300">{detail.change.description}</p>
          ) : (
            canEdit && <p className="text-sm italic text-slate-400">No description — click Edit to add one.</p>
          )}

          {/* Recipient */}
          {!editing && detail.change.assigneeActorId && (
            <p className="text-xs text-slate-500 dark:text-slate-400">
              Recipient:{' '}
              <span className="font-medium text-slate-700 dark:text-slate-200">
                {members.find((m) => m.actorId === detail.change.assigneeActorId)?.displayName ?? 'Unknown'}
              </span>
            </p>
          )}

          {/* Evidence */}
          {detail.evidence.length > 0 && (
            <Section title="Evidence">
              <ul className="space-y-1">
                {detail.evidence.map((o) => (
                  <li key={o.id} className="font-mono text-xs text-slate-600 dark:text-slate-300">
                    {o.payload} {o.occurrenceCount > 1 && <Badge tone="red">×{o.occurrenceCount}</Badge>}
                  </li>
                ))}
              </ul>
            </Section>
          )}

          {/* Comments */}
          <Section title="Comments">
            <ul className="space-y-2">
              {detail.comments.map((c) => (
                <li key={c.id} className="rounded-md bg-slate-50 p-2 dark:bg-slate-800">
                  <div className="mb-1">
                    <Badge tone={c.kind === 'AgentRecommendation' ? 'violet' : 'slate'}>
                      {c.kind === 'AgentRecommendation' ? 'agent' : 'human'}
                    </Badge>
                  </div>
                  <p className="whitespace-pre-wrap text-sm text-slate-700 dark:text-slate-200">{c.body}</p>
                </li>
              ))}
              {detail.comments.length === 0 && <li className="text-sm text-slate-400">No comments yet.</li>}
            </ul>

            <div className="mt-2 space-y-2">
              <Textarea value={commentBody} onChange={(e) => setCommentBody(e.target.value)} placeholder="Add a comment…" />
              <div className="flex flex-wrap items-center gap-2">
                <Button variant="default" disabled={!commentBody.trim() || addComment.isPending} onClick={() => addComment.mutate()}>
                  Comment
                </Button>
                {/* AI-agent commentary — hidden for Human-Only projects. */}
                {!humanOnly && <span className="mx-1 h-5 w-px bg-slate-200 dark:bg-slate-700" />}
                {!humanOnly && (agents.length > 0 ? (
                  <>
                    <Select value={agentId || agents[0].actorId} onChange={(e) => setAgentId(e.target.value)} className="max-w-40">
                      {agents.map((a) => <option key={a.actorId} value={a.actorId}>{a.displayName}</option>)}
                    </Select>
                    <Button variant="primary" disabled={askAgent.isPending} onClick={() => askAgent.mutate()}>
                      {askAgent.isPending ? 'Asking…' : 'Ask agent'}
                    </Button>
                  </>
                ) : (
                  <span className="text-xs text-slate-400">Add an Agent member to enable AI commentary.</span>
                ))}
              </div>
              <ErrorText error={addComment.error} />
              <ErrorText error={askAgent.error} />
            </div>
          </Section>

          {/* SCM links — hidden when the project has source control disabled */}
          {scmEnabled && (
          <Section title="Source control">
            <ul className="space-y-1">
              {detail.scmLinks.map((l) => (
                <li key={l.id} className="text-sm">
                  <a href={l.url} target="_blank" rel="noreferrer" className="text-indigo-600 hover:underline">{l.url}</a>
                  {l.contextSnapshot && <p className="mt-0.5 line-clamp-2 text-xs text-slate-500">{l.contextSnapshot}</p>}
                </li>
              ))}
              {detail.scmLinks.length === 0 && <li className="text-sm text-slate-400">No links.</li>}
            </ul>
            <div className="mt-2 flex gap-2">
              <TextInput value={scmUrl} onChange={(e) => setScmUrl(e.target.value)} placeholder="https://github.com/owner/repo/pull/1" />
              <Button variant="default" disabled={!scmUrl.trim() || addScm.isPending} onClick={() => addScm.mutate()}>Link</Button>
            </div>
            <ErrorText error={addScm.error} />
          </Section>
          )}

          {/* Audit trail */}
          <Section title="Audit trail">
            <ol className="space-y-1">
              {(audit ?? []).map((e) => (
                <li key={e.id} className="text-xs text-slate-500">
                  <span className="font-medium text-slate-700 dark:text-slate-300">{e.action}</span>
                  {e.fromStatus && <> · {e.fromStatus} → {e.toStatus}</>}
                  {!e.fromStatus && e.toStatus && <> · {e.toStatus}</>}
                  {e.reason && <> · “{e.reason}”</>}
                </li>
              ))}
            </ol>
          </Section>
        </div>
      )}
    </>
  )
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section>
      <h3 className="mb-1.5 text-xs font-semibold uppercase tracking-wide text-slate-400">{title}</h3>
      {children}
    </section>
  )
}
