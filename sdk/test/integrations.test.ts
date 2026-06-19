import { describe, expect, it } from 'vitest'
import { DimesClient } from '../src/client'
import { errorFingerprint, installErrorCapture } from '../src/integrations'
import { describeTarget, installRageClicks } from '../src/friction'
import type { FetchLike, IngestBody } from '../src/types'

function fakeTransport() {
  const calls: Array<IngestBody> = []
  const transport: FetchLike = (_url, init) => {
    calls.push(JSON.parse(init.body as string) as IngestBody)
    return Promise.resolve({ ok: true, status: 200 })
  }
  return { transport, calls }
}

const baseConfig = { endpoint: 'https://dimes.test', sourceId: 's' }

describe('errorFingerprint', () => {
  it('is stable for the same error', () => {
    const e = new TypeError('boom')
    expect(errorFingerprint(e, 'boom')).toBe(errorFingerprint(e, 'boom'))
  })
  it('differs for different messages', () => {
    expect(errorFingerprint(new Error('a'), 'a')).not.toBe(errorFingerprint(new Error('b'), 'b'))
  })
})

describe('describeTarget', () => {
  it('builds a tag#id.class selector', () => {
    const el = document.createElement('button')
    el.id = 'save'
    el.className = 'btn primary'
    expect(describeTarget(el)).toBe('button#save.btn.primary')
  })
  it('handles null', () => {
    expect(describeTarget(null)).toBe('unknown')
  })
})

describe('installErrorCapture', () => {
  it('captures window error events as TechnicalError', async () => {
    const { transport, calls } = fakeTransport()
    const client = new DimesClient({ ...baseConfig, transport })
    const dispose = installErrorCapture(client)

    window.dispatchEvent(new ErrorEvent('error', { message: 'kaboom', error: new Error('kaboom') }))
    await client.flush()

    expect(calls).toHaveLength(1)
    expect(calls[0].kind).toBe('TechnicalError')
    expect(calls[0].fingerprint).toContain('kaboom')
    dispose()
  })
})

describe('installRageClicks', () => {
  it('captures a BehavioralFriction signal after rapid repeated clicks', async () => {
    const { transport, calls } = fakeTransport()
    const client = new DimesClient({ ...baseConfig, transport })
    const dispose = installRageClicks(client, { threshold: 3, radius: 1000 })

    for (let i = 0; i < 3; i++) {
      document.dispatchEvent(new MouseEvent('click', { bubbles: true, clientX: 10, clientY: 10 }))
    }
    await client.flush()

    const rage = calls.find((c) => c.kind === 'BehavioralFriction')
    expect(rage).toBeDefined()
    expect(JSON.parse(rage!.payload).signal).toBe('rage-click')
    dispose()
  })
})
