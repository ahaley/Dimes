import { ContextTracker } from './context'
import type { CaptureEvent, CaptureInput, DimesConfig, FetchLike, IngestBody, UserContext } from './types'

/** The capture client. Framework-agnostic; posts one observation per signal to the ingest endpoint,
 * fire-and-forget (errors swallowed) so it never disrupts the host app. Use `flush()` to await
 * in-flight deliveries (e.g. in tests or before unload). */
export class DimesClient {
  readonly context: ContextTracker

  private readonly endpoint: string
  private readonly sourceId: string
  private readonly enabled: boolean
  private readonly sampleRate: number
  private readonly beforeSend?: (event: CaptureEvent) => CaptureEvent | null
  private readonly fetchImpl: FetchLike
  private readonly debug: boolean
  // Deliveries are serialized into a single chain: one POST at a time. This avoids a request
  // stampede and lets repeated same-fingerprint signals from one client aggregate server-side
  // (concurrent posts would otherwise race the read-then-insert).
  private deliveryChain: Promise<void> = Promise.resolve()
  private readonly teardowns: Array<() => void> = []

  constructor(config: DimesConfig) {
    this.endpoint = config.endpoint.replace(/\/+$/, '')
    this.sourceId = config.sourceId
    this.enabled = config.enabled ?? true
    this.sampleRate = config.sampleRate ?? 1
    this.beforeSend = config.beforeSend
    this.debug = config.debug ?? false
    this.context = new ContextTracker(config)
    this.fetchImpl = config.transport ?? defaultTransport()
  }

  identify(user: UserContext): void {
    this.context.identify(user)
  }

  addBreadcrumb(type: string, message: string): void {
    this.context.addBreadcrumb(type, message)
  }

  /** Low-level capture. `force` bypasses sampling (used for explicit feedback). */
  capture(input: CaptureInput, opts?: { force?: boolean }): void {
    if (!this.enabled) return
    if (!(opts?.force ?? false) && Math.random() >= this.sampleRate) return

    let event: CaptureEvent | null = {
      kind: input.kind,
      payload: typeof input.payload === 'string' ? { message: input.payload } : input.payload,
      fingerprint: input.fingerprint,
      context: this.context.build(),
    }
    if (this.beforeSend) event = this.beforeSend(event)
    if (!event) return

    const body: IngestBody = {
      kind: event.kind,
      payload: JSON.stringify(event.payload),
      contextMetadata: JSON.stringify(event.context),
      fingerprint: event.fingerprint ?? null,
    }

    this.deliveryChain = this.deliveryChain.then(() => this.deliver(body))
  }

  /** Explicit, user-initiated feedback — always sent (not sampled). */
  captureFeedback(message: string, extra?: Record<string, unknown>): void {
    this.capture({ kind: 'ExplicitFeedback', payload: { message, ...extra } }, { force: true })
    this.addBreadcrumb('feedback', message)
  }

  /** Await all queued deliveries. */
  async flush(): Promise<void> {
    await this.deliveryChain
  }

  registerTeardown(fn: () => void): void {
    this.teardowns.push(fn)
  }

  /** Remove installed listeners and flush. */
  async shutdown(): Promise<void> {
    for (const fn of this.teardowns.splice(0)) fn()
    await this.flush()
  }

  private async deliver(body: IngestBody): Promise<void> {
    const url = `${this.endpoint}/api/sources/${this.sourceId}/observations`
    try {
      const res = await this.fetchImpl(url, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
        keepalive: true,
      })
      if (!res.ok && this.debug) console.warn('[dimes] ingest failed:', res.status)
    } catch (err) {
      if (this.debug) console.warn('[dimes] ingest error:', err)
    }
  }
}

function defaultTransport(): FetchLike {
  return (url, init) => fetch(url, init).then((r) => ({ ok: r.ok, status: r.status }))
}
