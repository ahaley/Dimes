import type { ReactNode } from 'react'
import { useSources } from '../api/hooks'
import type { Observation } from '../api/types'
import { Badge } from '../components/ui'

type Json = Record<string, unknown>

// Payloads come from arbitrary host apps, so nothing here may assume a shape.
function tryParseObject(text: string | null | undefined): Json | null {
  if (!text) return null
  try {
    const v: unknown = JSON.parse(text)
    return v && typeof v === 'object' && !Array.isArray(v) ? (v as Json) : null
  } catch {
    return null
  }
}

function pretty(text: string | null | undefined): string {
  if (!text) return ''
  try { return JSON.stringify(JSON.parse(text), null, 2) } catch { return text }
}

function asText(v: unknown): string {
  return typeof v === 'string' ? v : JSON.stringify(v, null, 2)
}

const when = (iso: string) => new Date(iso).toLocaleString()

export function ObservationDetail({ observation: o }: { observation: Observation }) {
  const { data: sources } = useSources(o.projectId)
  const sourceName = sources?.find((s) => s.id === o.sourceId)?.name ?? o.sourceId

  const payload = tryParseObject(o.payload)
  const context = tryParseObject(o.contextMetadata)

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center gap-2">
        <Badge tone="amber">{o.kind}</Badge>
        {o.occurrenceCount > 1 && <Badge tone="red">×{o.occurrenceCount}</Badge>}
        {o.status === 'Clustered' && <Badge tone="slate">clustered</Badge>}
      </div>

      <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-xs">
        <Row label="Source">{sourceName}</Row>
        <Row label="First seen">{when(o.firstSeen)}</Row>
        <Row label="Last seen">{when(o.lastSeen)}</Row>
        {o.fingerprint && <Row label="Fingerprint" mono>{o.fingerprint}</Row>}
      </dl>

      <Block title="Payload">
        {payload ? (
          <dl className="space-y-2">
            {Object.entries(payload).filter(([, v]) => v !== undefined && v !== null).map(([k, v]) => (
              <Field key={k} name={k} value={v} />
            ))}
          </dl>
        ) : (
          <Pre>{o.payload}</Pre>
        )}
      </Block>

      {context && <ContextBlock context={context} />}

      <details className="text-xs">
        <summary className="cursor-pointer font-medium text-slate-500">Raw JSON</summary>
        <Pre>{pretty(o.payload)}</Pre>
        {o.contextMetadata && <Pre>{pretty(o.contextMetadata)}</Pre>}
      </details>
    </div>
  )
}

const KNOWN_CONTEXT = ['route', 'appVersion', 'release', 'userAgent', 'viewport', 'user', 'breadcrumbs']

function ContextBlock({ context: c }: { context: Json }) {
  const user = c.user && typeof c.user === 'object' ? (c.user as Json) : null
  const vp = c.viewport && typeof c.viewport === 'object' ? (c.viewport as { w?: unknown; h?: unknown }) : null
  const crumbs = Array.isArray(c.breadcrumbs) ? (c.breadcrumbs as Json[]) : []
  const other = Object.entries(c).filter(([k, v]) => !KNOWN_CONTEXT.includes(k) && v !== undefined && v !== null)

  return (
    <Block title="Context">
      <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1 text-xs">
        {typeof c.route === 'string' && <Row label="Route" mono>{c.route}</Row>}
        {typeof c.appVersion === 'string' && <Row label="Version">{c.appVersion}</Row>}
        {typeof c.release === 'string' && <Row label="Release">{c.release}</Row>}
        {user && Object.entries(user).map(([k, v]) => <Row key={k} label={`User ${k}`}>{asText(v)}</Row>)}
        {vp && vp.w != null && vp.h != null && <Row label="Viewport">{String(vp.w)} × {String(vp.h)}</Row>}
        {typeof c.userAgent === 'string' && <Row label="User agent">{c.userAgent}</Row>}
        {other.map(([k, v]) => <Row key={k} label={k}>{asText(v)}</Row>)}
      </dl>
      {crumbs.length > 0 && (
        <details className="mt-2 text-xs">
          <summary className="cursor-pointer font-medium text-slate-500">Breadcrumbs ({crumbs.length})</summary>
          <ol className="mt-1 space-y-1">
            {crumbs.map((b, i) => (
              <li key={i} className="flex gap-2">
                <span className="shrink-0 font-mono text-slate-400">
                  {typeof b.t === 'number' ? new Date(b.t).toLocaleTimeString() : ''}
                </span>
                <Badge tone="slate">{String(b.type ?? '')}</Badge>
                <span className="min-w-0 break-words text-slate-600 dark:text-slate-300">{String(b.message ?? '')}</span>
              </li>
            ))}
          </ol>
        </details>
      )}
    </Block>
  )
}

function Block({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section>
      <h3 className="mb-1 text-xs font-semibold uppercase tracking-wide text-slate-500">{title}</h3>
      {children}
    </section>
  )
}

function Row({ label, children, mono }: { label: string; children: ReactNode; mono?: boolean }) {
  return (
    <>
      <dt className="text-slate-500">{label}</dt>
      <dd className={`min-w-0 break-words text-slate-700 dark:text-slate-200${mono ? ' font-mono' : ''}`}>{children}</dd>
    </>
  )
}

function Field({ name, value }: { name: string; value: unknown }) {
  const text = asText(value)
  const block = text.includes('\n') || typeof value === 'object'
  return (
    <div>
      <dt className="text-xs text-slate-500">{name}</dt>
      <dd>
        {block ? <Pre>{text}</Pre> : <span className="break-words font-mono text-xs text-slate-700 dark:text-slate-200">{text}</span>}
      </dd>
    </div>
  )
}

function Pre({ children }: { children: ReactNode }) {
  return (
    <pre className="mt-1 max-h-72 overflow-auto whitespace-pre-wrap break-words rounded bg-slate-50 p-2 font-mono text-xs text-slate-600 dark:bg-slate-800 dark:text-slate-300">
      {children}
    </pre>
  )
}
