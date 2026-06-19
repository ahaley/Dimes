import type { DimesClient } from './client'

export interface RageClickOptions {
  threshold?: number
  windowMs?: number
  radius?: number
}

/** Detect rage clicks (rapid repeated clicks in the same spot) and capture them as a high-confidence
 * BehavioralFriction signal. Also records every click as a breadcrumb. Returns a disposer. */
export function installRageClicks(client: DimesClient, opts: RageClickOptions = {}): () => void {
  if (typeof window === 'undefined' || typeof document === 'undefined') return () => {}

  const threshold = opts.threshold ?? 4
  const windowMs = opts.windowMs ?? 1000
  const radius = opts.radius ?? 30
  let clicks: Array<{ x: number; y: number; t: number }> = []

  const onClick = (e: MouseEvent) => {
    const now = Date.now()
    clicks = clicks.filter((c) => now - c.t < windowMs)
    clicks.push({ x: e.clientX, y: e.clientY, t: now })
    client.addBreadcrumb('click', describeTarget(e.target))

    const near = clicks.filter((c) => Math.hypot(c.x - e.clientX, c.y - e.clientY) <= radius)
    if (near.length >= threshold) {
      clicks = []
      const selector = describeTarget(e.target)
      client.capture({
        kind: 'BehavioralFriction',
        payload: { signal: 'rage-click', selector, x: e.clientX, y: e.clientY, count: near.length },
        fingerprint: `rage:${selector}`,
      })
    }
  }

  document.addEventListener('click', onClick, true)
  return () => document.removeEventListener('click', onClick, true)
}

/** A compact, PII-free selector string for an event target (tag#id.class). */
export function describeTarget(target: EventTarget | null): string {
  const el = target as Element | null
  if (!el || !el.tagName) return 'unknown'
  const tag = el.tagName.toLowerCase()
  const id = (el as HTMLElement).id ? `#${(el as HTMLElement).id}` : ''
  const className = typeof el.className === 'string' ? el.className.trim() : ''
  const cls = className ? `.${className.split(/\s+/).join('.')}` : ''
  return `${tag}${id}${cls}`.slice(0, 100)
}
