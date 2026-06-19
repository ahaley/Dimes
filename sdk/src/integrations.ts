import type { DimesClient } from './client'

/** Auto-capture uncaught errors and unhandled promise rejections as TechnicalError observations.
 * Returns a disposer. No-op outside the browser. */
export function installErrorCapture(client: DimesClient): () => void {
  if (typeof window === 'undefined') return () => {}

  const onError = (event: ErrorEvent) => {
    client.capture({
      kind: 'TechnicalError',
      payload: errorPayload(event.error, event.message),
      fingerprint: errorFingerprint(event.error, event.message),
    })
  }
  const onRejection = (event: PromiseRejectionEvent) => {
    const reason = event.reason
    const err = reason instanceof Error ? reason : undefined
    client.capture({
      kind: 'TechnicalError',
      payload: errorPayload(err, String(reason)),
      fingerprint: errorFingerprint(err, String(reason)),
    })
  }

  window.addEventListener('error', onError)
  window.addEventListener('unhandledrejection', onRejection)
  return () => {
    window.removeEventListener('error', onError)
    window.removeEventListener('unhandledrejection', onRejection)
  }
}

export function errorPayload(err: Error | undefined, fallbackMessage: string): Record<string, unknown> {
  return {
    name: err?.name ?? 'Error',
    message: err?.message ?? fallbackMessage,
    stack: err?.stack ? err.stack.split('\n').slice(0, 5).join('\n') : undefined,
  }
}

/** Stable fingerprint so repeated occurrences of the same error aggregate server-side. */
export function errorFingerprint(err: Error | undefined, fallbackMessage: string): string {
  const name = err?.name ?? 'Error'
  const message = err?.message ?? fallbackMessage
  return `err:${name}:${message}:${firstFrame(err?.stack)}`.slice(0, 200)
}

function firstFrame(stack?: string): string {
  if (!stack) return ''
  const lines = stack.split('\n').map((l) => l.trim())
  return lines.find((l) => l.startsWith('at ')) ?? ''
}
