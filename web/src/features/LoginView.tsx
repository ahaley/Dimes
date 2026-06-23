import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { api } from '../api/client'
import { keys, useAuthConfig, useSiteBranding } from '../api/hooks'
import { Button, Card, ErrorText, Field, TextInput } from '../components/ui'

/** The unauthenticated gate. Local mode shows an email/password form; OIDC mode shows a button that
 * navigates to the API challenge endpoint (a real navigation so the browser follows the redirect). */
export function LoginView() {
  const { data: config } = useAuthConfig()
  const { data: branding } = useSiteBranding()
  const qc = useQueryClient()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')

  const login = useMutation({
    mutationFn: () => api.login({ email, password }),
    onSuccess: (me) => qc.setQueryData(keys.me, me),
  })

  return (
    <div className="flex min-h-screen items-center justify-center bg-slate-50 p-4 dark:bg-slate-950">
      <Card className="w-full max-w-sm space-y-5 p-6">
        <div className="text-center">
          <div className="text-2xl font-semibold tracking-tight text-indigo-700">{branding?.title ?? 'Dimes'}</div>
          <p className="mt-1 text-sm text-slate-500">Sign in to continue</p>
        </div>

        {config?.mode === 'Oidc' ? (
          <Button
            variant="primary"
            className="w-full"
            onClick={() => { window.location.href = '/api/auth/challenge' }}
          >
            Sign in with Keycloak
          </Button>
        ) : (
          <form className="space-y-3" onSubmit={(e) => { e.preventDefault(); login.mutate() }}>
            <Field label="Email">
              <TextInput type="email" value={email} onChange={(e) => setEmail(e.target.value)} autoFocus />
            </Field>
            <Field label="Password">
              <TextInput type="password" value={password} onChange={(e) => setPassword(e.target.value)} />
            </Field>
            <ErrorText error={login.error} />
            <Button
              type="submit"
              variant="primary"
              className="w-full"
              disabled={!email.trim() || !password || login.isPending}
            >
              {login.isPending ? 'Signing in…' : 'Sign in'}
            </Button>
          </form>
        )}
      </Card>
    </div>
  )
}
