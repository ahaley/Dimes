import { useEffect, useMemo, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { keys, useAuthConfig, useProjects, useSiteBranding, useSiteUsers, useUpdateSiteBranding, useUserMemberships } from '../api/hooks'
import type { MemberRole, SiteUser } from '../api/types'
import { Badge, Button, Card, cx, ErrorText, Field, Modal, Select, TextInput } from '../components/ui'
import { OverflowMenu, type MenuAction } from '../components/OverflowMenu'
import { useToast } from '../components/Toast'
import { initials } from '../lifecycle'

type SortKey = 'name' | 'status'
type SortState = { key: SortKey; dir: 'asc' | 'desc' }

// Status grouping for sorting: site admins, then active, then (local only) pre-provisioned
// (no password), then archived. "No password" is meaningless in OIDC, so it doesn't group there.
function statusRank(u: SiteUser, isLocal: boolean): number {
  if (u.isArchived) return 3
  if (isLocal && !u.hasLocalCredential) return 2
  if (u.isSiteAdmin) return 0
  return 1
}

/** Site-admin screen: shows the (deployment-configured) auth mode and, in local mode, manages users. */
export function SiteSettingsView() {
  const { data: config } = useAuthConfig()
  const isLocal = config?.mode === 'Local'
  // Load users in both modes. Local adds password management; in OIDC users are JIT-provisioned, but
  // a site admin still manages roles, archival, project access, and pre-provisioning here.
  const { data: users } = useSiteUsers(true)
  // The projects modal is lifted out of the row so it isn't rendered inside the users <table>.
  const [managingUser, setManagingUser] = useState<SiteUser | null>(null)

  // Filter + sort happen in memory so the table stays responsive for large user lists.
  const [query, setQuery] = useState('')
  const [sort, setSort] = useState<SortState>({ key: 'name', dir: 'asc' })

  const visibleUsers = useMemo(() => {
    const q = query.trim().toLowerCase()
    const filtered = q
      ? (users ?? []).filter(
          (u) => u.displayName.toLowerCase().includes(q) || (u.email ?? '').toLowerCase().includes(q),
        )
      : (users ?? [])
    const sign = sort.dir === 'asc' ? 1 : -1
    return [...filtered].sort((a, b) => {
      const cmp =
        sort.key === 'name'
          ? a.displayName.localeCompare(b.displayName)
          : statusRank(a, isLocal) - statusRank(b, isLocal) || a.displayName.localeCompare(b.displayName)
      return cmp * sign
    })
  }, [users, query, sort, isLocal])

  const toggleSort = (key: SortKey) =>
    setSort((s) => (s.key === key ? { key, dir: s.dir === 'asc' ? 'desc' : 'asc' } : { key, dir: 'asc' }))
  const sortArrow = (key: SortKey) => (sort.key === key ? (sort.dir === 'asc' ? ' ▲' : ' ▼') : '')

  return (
    <div className="mx-auto max-w-2xl space-y-4">
      <div>
        <h1 className="text-lg font-semibold text-slate-800 dark:text-slate-100">Site settings</h1>
        <p className="mt-1 text-sm text-slate-500">
          Application-wide configuration. Authentication mode is chosen via deployment config and
          shown here for reference.
        </p>
      </div>

      <Card className="space-y-1 p-4">
        <div className="flex items-center gap-2">
          <span className="text-sm font-medium text-slate-700 dark:text-slate-200">Authentication mode</span>
          <Badge tone="indigo">{config?.mode ?? '…'}</Badge>
        </div>
        <p className="text-xs text-slate-500">
          {isLocal
            ? 'Local email + password sessions. Manage users below.'
            : 'OpenID Connect (Keycloak). Users are provisioned on first sign-in; manage their roles, access, and pre-provisioning below.'}
          {' '}Changing the mode is a deployment-config change and requires an API restart.
        </p>
      </Card>

      <BrandingCard />

      <div className="flex items-center gap-2">
        <TextInput
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Filter by name or email…"
          className="max-w-xs"
        />
        {query && (
          <span className="text-xs text-slate-400">
            {visibleUsers.length} of {users?.length ?? 0}
          </span>
        )}
      </div>
      <Card className="sm:max-h-[60vh] sm:overflow-auto">
        <table className="min-w-full text-sm">
          <thead>
            <tr className="border-b border-slate-200 text-left text-xs font-semibold uppercase tracking-wide text-slate-400 dark:border-slate-800">
              <th className="sticky top-0 z-10 bg-white px-3 py-2 font-semibold dark:bg-slate-900">
                <button type="button" className="font-semibold uppercase tracking-wide hover:text-slate-600 dark:hover:text-slate-200" onClick={() => toggleSort('name')}>
                  User{sortArrow('name')}
                </button>
              </th>
              <th className="sticky top-0 z-10 bg-white px-3 py-2 font-semibold dark:bg-slate-900">
                <button type="button" className="font-semibold uppercase tracking-wide hover:text-slate-600 dark:hover:text-slate-200" onClick={() => toggleSort('status')}>
                  Status{sortArrow('status')}
                </button>
              </th>
              <th className="sticky top-0 z-10 bg-white px-3 py-2 text-right font-semibold dark:bg-slate-900">Actions</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100 dark:divide-slate-800">
            {visibleUsers.map((u) => (
              <UserRow key={u.id} user={u} isLocal={isLocal} onManageProjects={() => setManagingUser(u)} />
            ))}
            {visibleUsers.length === 0 && (
              <tr><td colSpan={3} className="px-3 py-4 text-sm text-slate-400">{users?.length ? 'No users match.' : 'No users yet.'}</td></tr>
            )}
          </tbody>
        </table>
      </Card>
      <CreateUserForm isLocal={isLocal} />

      {managingUser && (
        <ManageProjectsModal user={managingUser} onClose={() => setManagingUser(null)} />
      )}
    </div>
  )
}

/** Site-admin form to set the custom site title (brand). */
function BrandingCard() {
  const { data: branding } = useSiteBranding()
  const update = useUpdateSiteBranding()
  const toast = useToast()
  const [title, setTitle] = useState('')
  // Prefill from the loaded branding (and re-sync after a successful save).
  useEffect(() => { if (branding) setTitle(branding.title) }, [branding])

  const trimmed = title.trim()
  const dirty = !!branding && trimmed.length > 0 && trimmed !== branding.title

  return (
    <Card className="space-y-2 p-4">
      <h2 className="text-sm font-semibold text-slate-700 dark:text-slate-200">Branding</h2>
      <Field label="Site title">
        <TextInput value={title} maxLength={60} onChange={(e) => setTitle(e.target.value)} placeholder="Dimes" className="max-w-xs" />
      </Field>
      <p className="text-xs text-slate-400">Shown in the sidebar, on the login screen, and in the browser tab. Up to 60 characters.</p>
      <ErrorText error={update.error} />
      <Button
        variant="primary"
        disabled={!dirty || update.isPending}
        onClick={() => update.mutate({ title: trimmed }, { onSuccess: () => toast.success('Site title updated') })}
      >
        Save
      </Button>
    </Card>
  )
}

function UserRow({ user, isLocal, onManageProjects }: { user: SiteUser; isLocal: boolean; onManageProjects: () => void }) {
  const qc = useQueryClient()
  const toast = useToast()
  const invalidate = () => qc.invalidateQueries({ queryKey: keys.users })
  const onError = (verb: string) => (e: unknown) => toast.error(e instanceof Error ? e.message : `Could not ${verb}`)

  const [editing, setEditing] = useState(false)
  const [displayName, setDisplayName] = useState(user.displayName)
  const [email, setEmail] = useState(user.email ?? '')

  const save = useMutation({
    mutationFn: () => api.updateUser(user.id, { displayName, email: email || null }),
    onSuccess: () => { invalidate(); setEditing(false); toast.success(`Updated ${displayName}`) },
    onError: onError('update user'),
  })
  const resetPassword = useMutation({
    mutationFn: (password: string) => api.resetPassword(user.id, { password }),
    onSuccess: () => toast.success(`Password reset for ${user.displayName}`),
    onError: onError('reset password'),
  })
  const toggleAdmin = useMutation({
    mutationFn: () => api.setSiteAdmin(user.id, { isSiteAdmin: !user.isSiteAdmin }),
    onSuccess: () => { invalidate(); toast.success(`Updated ${user.displayName}`) },
    onError: onError('update user'),
  })
  const archive = useMutation({
    mutationFn: () => (user.isArchived ? api.unarchiveUser(user.id) : api.archiveUser(user.id)),
    onSuccess: () => { invalidate(); toast.success(`${user.isArchived ? 'Unarchived' : 'Archived'} ${user.displayName}`) },
    onError: onError('update user'),
  })
  const remove = useMutation({
    mutationFn: () => api.deleteUser(user.id),
    onSuccess: () => { invalidate(); toast.success(`Deleted ${user.displayName}`) },
    onError: onError('delete user'),
  })

  if (editing) {
    return (
      <tr>
        <td colSpan={3} className="px-3 py-3">
          <div className="space-y-2">
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
              <Field label="Display name"><TextInput value={displayName} onChange={(e) => setDisplayName(e.target.value)} autoFocus /></Field>
              <Field label="Email"><TextInput type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></Field>
            </div>
            <ErrorText error={save.error} />
            <div className="flex justify-end gap-2">
              <Button
                variant="subtle"
                onClick={() => { setDisplayName(user.displayName); setEmail(user.email ?? ''); setEditing(false) }}
              >
                Cancel
              </Button>
              <Button variant="primary" disabled={!displayName.trim() || save.isPending} onClick={() => save.mutate()}>Save</Button>
            </div>
          </div>
        </td>
      </tr>
    )
  }

  const lockReason = 'Referenced by changes, comments, or audit history — archive instead of deleting.'
  // Single source of truth for row actions — rendered inline on desktop, folded into a ⋯ menu on phones.
  const actions: MenuAction[] = [
    { label: 'Edit', onClick: () => setEditing(true) },
    { label: 'Projects', onClick: onManageProjects },
    ...(isLocal
      ? [{
          label: 'Reset password',
          disabled: resetPassword.isPending,
          onClick: () => { const pw = window.prompt(`New password for ${user.displayName}:`); if (pw) resetPassword.mutate(pw) },
        }]
      : []),
    { label: user.isSiteAdmin ? 'Revoke admin' : 'Make admin', disabled: toggleAdmin.isPending, onClick: () => toggleAdmin.mutate() },
    {
      label: user.isArchived ? 'Unarchive' : 'Archive',
      disabled: archive.isPending,
      title: user.isArchived ? 'Restore sign-in access' : 'Block sign-in; keep history',
      onClick: () => archive.mutate(),
    },
    {
      label: 'Delete',
      disabled: !user.deletable || remove.isPending,
      title: user.deletable ? 'Delete user' : lockReason,
      onClick: () => { if (window.confirm(`Delete user "${user.displayName}"?`)) remove.mutate() },
    },
  ]
  return (
    <tr className={cx('align-top', user.isArchived && 'opacity-70')}>
      {/* Identity: avatar + name, email beneath. */}
      <td className="px-3 py-3">
        <div className="flex min-w-0 items-center gap-3">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-slate-100 text-xs font-semibold text-slate-600 dark:bg-slate-800 dark:text-slate-300">
            {initials(user.displayName)}
          </div>
          <div className="min-w-0">
            <div className="truncate font-medium text-slate-800 dark:text-slate-100">{user.displayName}</div>
            <div className="truncate text-xs text-slate-400">{user.email ?? 'no email'}</div>
          </div>
        </div>
      </td>

      {/* Status badges. */}
      <td className="px-3 py-3">
        <div className="flex flex-wrap items-center gap-1">
          {user.isSiteAdmin && <Badge tone="violet">site admin</Badge>}
          {user.isArchived && <Badge tone="amber">archived</Badge>}
          {isLocal && !user.hasLocalCredential && <Badge tone="slate">no password</Badge>}
          {!user.isSiteAdmin && !user.isArchived && (!isLocal || user.hasLocalCredential) && (
            <span className="text-xs text-slate-400">active</span>
          )}
        </div>
      </td>

      {/* Actions: inline buttons on desktop; folded into a ⋯ menu on phones to keep the row compact. */}
      <td className="px-3 py-3">
        <div className="hidden flex-wrap justify-end gap-1 sm:flex">
          {actions.map((a, i) => (
            <Button key={i} variant="subtle" disabled={a.disabled} title={a.title} onClick={a.onClick}>
              {a.label}
            </Button>
          ))}
        </div>
        <div className="flex justify-end sm:hidden">
          <OverflowMenu actions={actions} label="User actions" variant="subtle" />
        </div>
      </td>
    </tr>
  )
}

function CreateUserForm({ isLocal }: { isLocal: boolean }) {
  const qc = useQueryClient()
  const toast = useToast()
  const [displayName, setDisplayName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [isSiteAdmin, setIsSiteAdmin] = useState(false)

  const create = useMutation({
    mutationFn: () => api.createLocalUser({ displayName, email, password: password || null, isSiteAdmin }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: keys.users })
      setDisplayName(''); setEmail(''); setPassword(''); setIsSiteAdmin(false)
      toast.success('User created')
    },
  })

  return (
    <Card className="space-y-2 p-4">
      <h2 className="text-sm font-semibold text-slate-700 dark:text-slate-200">Add user</h2>
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
        <Field label="Display name"><TextInput value={displayName} onChange={(e) => setDisplayName(e.target.value)} /></Field>
        <Field label="Email"><TextInput type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></Field>
      </div>
      {isLocal && (
        <Field label="Password (optional)"><TextInput type="password" value={password} onChange={(e) => setPassword(e.target.value)} /></Field>
      )}
      <p className="text-xs text-slate-400">
        {isLocal
          ? 'Leave blank to pre-provision — they can sign in once a password is set (Reset password) or via your identity provider.'
          : 'Pre-provisions the user by email so you can grant project access before their first sign-in. They sign in via your identity provider.'}
      </p>
      <label className="flex items-center gap-2 text-xs text-slate-600 dark:text-slate-300">
        <input type="checkbox" checked={isSiteAdmin} onChange={(e) => setIsSiteAdmin(e.target.checked)} />
        Site administrator
      </label>
      <ErrorText error={create.error} />
      <Button
        variant="primary"
        disabled={!displayName.trim() || !email.trim() || create.isPending}
        onClick={() => create.mutate()}
      >
        Add user
      </Button>
    </Card>
  )
}

const ROLES: MemberRole[] = ['Reporter', 'Contributor', 'Maintainer']

/** Manage a user's project memberships — assign to projects, change role, remove. */
function ManageProjectsModal({ user, onClose }: { user: SiteUser; onClose: () => void }) {
  const qc = useQueryClient()
  const toast = useToast()
  const { data: memberships } = useUserMemberships(user.id)
  const { data: projects } = useProjects()
  const [projectId, setProjectId] = useState('')
  const [role, setRole] = useState<MemberRole>('Contributor')

  const invalidate = () => qc.invalidateQueries({ queryKey: ['user-memberships', user.id] })
  const onError = (verb: string) => (e: unknown) => toast.error(e instanceof Error ? e.message : `Could not ${verb}`)

  const assign = useMutation({
    mutationFn: (vars: { projectId: string; role: MemberRole }) => api.assignUserMembership(user.id, vars),
    onSuccess: () => { invalidate(); setProjectId('') },
    onError: onError('assign project'),
  })
  const remove = useMutation({
    mutationFn: (pid: string) => api.removeUserMembership(user.id, pid),
    onSuccess: invalidate,
    onError: onError('remove project'),
  })

  const memberOf = new Set((memberships ?? []).map((m) => m.projectId))
  const available = (projects ?? []).filter((p) => !memberOf.has(p.id))

  return (
    <Modal title={`Projects — ${user.displayName}`} onClose={onClose}>
      <div className="space-y-4">
        <ul className="space-y-1">
          {(memberships ?? []).map((m) => (
            <li key={m.projectId} className="flex items-center gap-2 text-sm">
              <span className="min-w-0 flex-1 truncate text-slate-800 dark:text-slate-100">{m.projectName}</span>
              <Select
                value={m.role}
                className="max-w-36"
                onChange={(e) => assign.mutate({ projectId: m.projectId, role: e.target.value as MemberRole })}
              >
                {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
              </Select>
              <Button variant="subtle" disabled={remove.isPending} onClick={() => remove.mutate(m.projectId)}>Remove</Button>
            </li>
          ))}
          {memberships?.length === 0 && <li className="text-sm text-slate-400">Not a member of any project.</li>}
        </ul>

        <div className="space-y-2 rounded-md border border-slate-200 p-3 dark:border-slate-700">
          <h3 className="text-xs font-semibold uppercase tracking-wide text-slate-400">Add to project</h3>
          {available.length === 0 ? (
            <p className="text-sm text-slate-400">Already in every project.</p>
          ) : (
            <div className="flex flex-col gap-2 sm:flex-row sm:items-end">
              <Field label="Project">
                <Select value={projectId} onChange={(e) => setProjectId(e.target.value)}>
                  <option value="">Select…</option>
                  {available.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                </Select>
              </Field>
              <Field label="Role">
                <Select value={role} onChange={(e) => setRole(e.target.value as MemberRole)}>
                  {ROLES.map((r) => <option key={r} value={r}>{r}</option>)}
                </Select>
              </Field>
              <Button
                variant="primary"
                disabled={!projectId || assign.isPending}
                onClick={() => assign.mutate({ projectId, role })}
              >
                Add
              </Button>
            </div>
          )}
          <ErrorText error={assign.error ?? remove.error} />
        </div>
      </div>
    </Modal>
  )
}
