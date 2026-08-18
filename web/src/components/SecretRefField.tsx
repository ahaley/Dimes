import { useId, type ReactNode } from 'react'
import { Button, Field, TextInput } from './ui'

/** The secret namespaces a web form can bind into, and the four routes each one resolves through.
 *
 * A hand-mirror of `ConfigSection` / `EnvironmentPrefix` in
 * `src/Dimes.Infrastructure/Providers/ConfigurationSecretResolver.cs` — the same deliberate duplication
 * as `api/types.ts` (the C# contracts) and `lifecycle.ts` (the transition table), so keep the two in
 * sync when a namespace changes.
 *
 * The resolver also has `Scm:` / `DIMES_SCM_`, absent here only because no web form binds an SCM token
 * yet. `Operator` is absent and must stay absent: it is the *unprefixed* namespace, and the entire point
 * of the split is that a name typed into a form can never address it. */
const NAMESPACES = {
  Llm: { config: 'Secrets:Llm:', file: 'SecretFiles:Llm:', env: 'DIMES_LLM_' },
  Notification: { config: 'Secrets:Notification:', file: 'SecretFiles:Notification:', env: 'DIMES_NOTIFICATION_' },
} as const

export type SecretNamespace = keyof typeof NAMESPACES

/** One binding, split so the fixed segments read as scenery and the typed name as the variable.
 *
 * The emphasis brightens the name rather than dimming the prefix, because the hint block is already the
 * dim tone — there is nothing below it to dim to. */
function Binding({ prefix, name, suffix }: { prefix: string; name: string; suffix?: string }) {
  return (
    <code className="font-mono">
      {prefix}
      <span className="font-semibold text-slate-600 dark:text-slate-200">{name}</span>
      {suffix}
    </code>
  )
}

/** The prefix a value already carries, or null.
 *
 * Matched case-insensitively because both routes are: `IConfiguration` keys are case-insensitive by
 * contract, and environment lookups are on Windows. Returns the matched span rather than a boolean so
 * the fix can slice off exactly what was found — and so the warning can show the doubling in whichever
 * route was echoed. */
function echoedPrefix(trimmed: string, namespace: SecretNamespace): string | null {
  const { config, file, env } = NAMESPACES[namespace]
  const lower = trimmed.toLowerCase()
  return [env, file, config].find((p) => lower.startsWith(p.toLowerCase())) ?? null
}

/** A secret *reference* field: the name a credential is bound under, never the credential.
 *
 * The field takes only the middle of the binding — the resolver supplies the namespace — so the fixed
 * prefix is rendered inside the control and every example below splits static from typed. Both halves of
 * that split have been got wrong in practice: binding the name unprefixed (resolves to nothing since
 * namespacing landed), then pasting the prefixed name back into the form (resolves as a doubled
 * prefix). Both save cleanly and fail only on first use, which is why this is stated at the input rather
 * than left to the operator to infer. */
export function SecretRefField({
  label, namespace, value, onChange, placeholder, disabled, requirement,
}: {
  label: string
  namespace: SecretNamespace
  value: string
  onChange: (next: string) => void
  placeholder: string
  disabled?: boolean
  requirement: ReactNode
}) {
  const hintId = useId()
  const ns = NAMESPACES[namespace]
  const trimmed = value.trim()
  // Before anything is typed the examples fall back to the placeholder, so they read as a concrete,
  // copyable pair rather than a shape with a hole in it.
  const name = trimmed || placeholder
  const echoed = disabled ? null : echoedPrefix(trimmed, namespace)

  return (
    <>
      <Field label={label}>
        <TextInput
          affix={ns.env}
          aria-describedby={hintId}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={placeholder}
          disabled={disabled}
        />
      </Field>

      {/* Not stripped silently: a reference legitimately may start with these characters — the resolver
          imposes no charset rule on one, since a prefix it can only extend leaves nothing to escape
          from — so rewriting the value would make such a name impossible to enter and would hide the
          mistake. An edit form prefills from the server, so this also surfaces an already-saved bad
          reference the moment it is opened. */}
      {echoed && (
        <div
          role="status"
          className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-amber-200 bg-amber-50/40 px-2.5 py-2 text-xs text-amber-900 dark:border-amber-900/60 dark:bg-amber-950/20 dark:text-amber-200"
        >
          <span>
            <code className="font-mono">{echoed}</code> is added for you — as typed, this resolves as{' '}
            <code className="font-mono">{echoed}{trimmed}</code>.
          </span>
          <Button className="shrink-0" onClick={() => onChange(trimmed.slice(echoed.length))}>
            Remove prefix
          </Button>
        </div>
      )}

      <div id={hintId} className="space-y-1 text-xs text-slate-400">
        <p>
          A name, not the credential itself — <code className="font-mono">{ns.env}</code> is added for
          you. Bind it to one of:
        </p>
        <ul className="list-disc space-y-0.5 pl-4">
          <li>
            <Binding prefix={ns.config} name={name} /> or <Binding prefix={ns.env} name={name} /> — the
            credential
          </li>
          <li>
            <Binding prefix={ns.file} name={name} /> or <Binding prefix={ns.env} name={name} suffix="_FILE" />{' '}
            — its file path, which Dimes reads
          </li>
        </ul>
        {/* The other half of the same confusion: _FILE selects the file route as a suffix on the
            binding, so putting it in the name just makes a literal binding with a longer name. That
            resolves, so this is guidance rather than a warning. */}
        {trimmed.toUpperCase().endsWith('_FILE') && (
          <p>
            Ending the name in <code className="font-mono">_FILE</code> doesn&apos;t choose the file
            route — the suffix belongs on the binding, and Dimes adds it.
          </p>
        )}
        <p>
          The <code className="font-mono">{namespace}</code> section is required: it keeps this name from
          reaching secrets bound elsewhere. {requirement}
        </p>
      </div>
    </>
  )
}
