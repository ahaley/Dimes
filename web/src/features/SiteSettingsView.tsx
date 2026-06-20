import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { keys, useAuthConfig, useSiteUsers } from '../api/hooks'
import type { SiteUser } from '../api/types'
import { Badge, Button, Card, cx, ErrorText, Field, TextInput } from '../components/ui'
import { useToast } from '../components/Toast'
import { initials } from '../lifecycle'

/** Site-admin screen: shows the (deployment-configured) auth mode and, in local mode, manages users. */
export function SiteSettingsView() {
  const { data: config } = useAuthConfig()
  const isLocal = config?.mode === 'Local'
  const { data: users } = useSiteUsers(isLocal)

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
            : 'OpenID Connect (Keycloak). Users are provisioned on first sign-in; grant project access via a project’s Manage screen.'}
          {' '}Changing the mode is a deployment-config change and requires an API restart.
        </p>
      </Card>

      {isLocal && (
        <>
          <Card className="divide-y divide-slate-100 dark:divide-slate-800">
            {(users ?? []).map((u) => <UserRow key={u.id} user={u} />)}
            {users?.length === 0 && <p className="p-4 text-sm text-slate-400">No users yet.</p>}
          </Card>
          <CreateUserForm />
        </>
      )}
    </div>
  )
}

function UserRow({ user }: { user: SiteUser }) {
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
      <div className="space-y-2 p-3">
        <div className="grid grid-cols-2 gap-2">
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
    )
  }

  const lockReason = 'Referenced by changes, comments, or audit history — archive instead of deleting.'
  return (
    <div className={cx('p-3', user.isArchived && 'opacity-70')}>
      {/* Identity: avatar + name/badges that truncate, email beneath. Never overflows. */}
      <div className="flex min-w-0 items-center gap-3">
        <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-slate-100 text-xs font-semibold text-slate-600 dark:bg-slate-800 dark:text-slate-300">
          {initials(user.displayName)}
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <span className="min-w-0 truncate font-medium text-slate-800 dark:text-slate-100">{user.displayName}</span>
            {user.isSiteAdmin && <Badge tone="violet">site admin</Badge>}
            {user.isArchived && <Badge tone="amber">archived</Badge>}
            {!user.hasLocalCredential && <Badge tone="slate">no password</Badge>}
          </div>
          <div className="truncate text-xs text-slate-400">{user.email ?? 'no email'}</div>
        </div>
      </div>

      {/* Actions: right-aligned, wrap onto a second line on narrow widths instead of overflowing. */}
      <div className="mt-2 flex flex-wrap justify-end gap-1">
        <Button variant="subtle" onClick={() => setEditing(true)}>Edit</Button>
        <Button
          variant="subtle"
          disabled={resetPassword.isPending}
          onClick={() => {
            const pw = window.prompt(`New password for ${user.displayName}:`)
            if (pw) resetPassword.mutate(pw)
          }}
        >
          Reset password
        </Button>
        <Button variant="subtle" disabled={toggleAdmin.isPending} onClick={() => toggleAdmin.mutate()}>
          {user.isSiteAdmin ? 'Revoke admin' : 'Make admin'}
        </Button>
        <Button
          variant="subtle"
          disabled={archive.isPending}
          title={user.isArchived ? 'Restore sign-in access' : 'Block sign-in; keep history'}
          onClick={() => archive.mutate()}
        >
          {user.isArchived ? 'Unarchive' : 'Archive'}
        </Button>
        <Button
          variant="subtle"
          disabled={!user.deletable || remove.isPending}
          title={user.deletable ? 'Delete user' : lockReason}
          onClick={() => { if (window.confirm(`Delete user "${user.displayName}"?`)) remove.mutate() }}
        >
          Delete
        </Button>
      </div>
    </div>
  )
}

function CreateUserForm() {
  const qc = useQueryClient()
  const toast = useToast()
  const [displayName, setDisplayName] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [isSiteAdmin, setIsSiteAdmin] = useState(false)

  const create = useMutation({
    mutationFn: () => api.createLocalUser({ displayName, email, password, isSiteAdmin }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: keys.users })
      setDisplayName(''); setEmail(''); setPassword(''); setIsSiteAdmin(false)
      toast.success('User created')
    },
  })

  return (
    <Card className="space-y-2 p-4">
      <h2 className="text-sm font-semibold text-slate-700 dark:text-slate-200">Add local user</h2>
      <div className="grid grid-cols-2 gap-2">
        <Field label="Display name"><TextInput value={displayName} onChange={(e) => setDisplayName(e.target.value)} /></Field>
        <Field label="Email"><TextInput type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></Field>
      </div>
      <Field label="Password"><TextInput type="password" value={password} onChange={(e) => setPassword(e.target.value)} /></Field>
      <label className="flex items-center gap-2 text-xs text-slate-600 dark:text-slate-300">
        <input type="checkbox" checked={isSiteAdmin} onChange={(e) => setIsSiteAdmin(e.target.checked)} />
        Site administrator
      </label>
      <ErrorText error={create.error} />
      <Button
        variant="primary"
        disabled={!displayName.trim() || !email.trim() || !password || create.isPending}
        onClick={() => create.mutate()}
      >
        Add user
      </Button>
    </Card>
  )
}
