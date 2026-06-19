import { describe, expect, it, vi } from 'vitest'
import { DimesClient } from '../src/client'
import type { FetchLike, IngestBody } from '../src/types'

function fakeTransport() {
  const calls: Array<{ url: string; body: IngestBody }> = []
  const transport: FetchLike = (url, init) => {
    calls.push({ url, body: JSON.parse(init.body as string) as IngestBody })
    return Promise.resolve({ ok: true, status: 200 })
  }
  return { transport, calls }
}

const baseConfig = { endpoint: 'https://dimes.test/', sourceId: 'src-1' }

describe('DimesClient', () => {
  it('posts to the ingest endpoint with the right shape', async () => {
    const { transport, calls } = fakeTransport()
    const client = new DimesClient({ ...baseConfig, transport })

    client.captureFeedback('export is broken', { area: 'reports' })
    await client.flush()

    expect(calls).toHaveLength(1)
    expect(calls[0].url).toBe('https://dimes.test/api/sources/src-1/observations')
    expect(calls[0].body.kind).toBe('ExplicitFeedback')
    const payload = JSON.parse(calls[0].body.payload)
    expect(payload.message).toBe('export is broken')
    expect(payload.area).toBe('reports')
    // context metadata is attached and serialized
    const ctx = JSON.parse(calls[0].body.contextMetadata as string)
    expect(ctx).toHaveProperty('route')
  })

  it('respects enabled=false', async () => {
    const { transport, calls } = fakeTransport()
    const client = new DimesClient({ ...baseConfig, enabled: false, transport })
    client.captureFeedback('nope')
    await client.flush()
    expect(calls).toHaveLength(0)
  })

  it('drops passive captures when sampleRate is 0 but still sends forced feedback', async () => {
    const { transport, calls } = fakeTransport()
    const client = new DimesClient({ ...baseConfig, sampleRate: 0, transport })

    client.capture({ kind: 'BehavioralFriction', payload: { signal: 'x' } }) // sampled out
    client.captureFeedback('forced') // forced
    await client.flush()

    expect(calls).toHaveLength(1)
    expect(calls[0].body.kind).toBe('ExplicitFeedback')
  })

  it('beforeSend can redact or drop events', async () => {
    const { transport, calls } = fakeTransport()
    const client = new DimesClient({
      ...baseConfig,
      transport,
      beforeSend: (e) => (e.kind === 'TechnicalError' ? null : { ...e, payload: { redacted: true } }),
    })

    client.capture({ kind: 'TechnicalError', payload: { secret: 's' } }, { force: true }) // dropped
    client.captureFeedback('keep me') // redacted
    await client.flush()

    expect(calls).toHaveLength(1)
    expect(JSON.parse(calls[0].body.payload)).toEqual({ redacted: true })
  })

  it('includes identify() data in context', async () => {
    const { transport, calls } = fakeTransport()
    const client = new DimesClient({ ...baseConfig, transport })
    client.identify({ role: 'admin', segment: 'beta' })
    client.captureFeedback('hi')
    await client.flush()
    const ctx = JSON.parse(calls[0].body.contextMetadata as string)
    expect(ctx.user).toEqual({ role: 'admin', segment: 'beta' })
  })

  it('never throws when transport fails', async () => {
    const transport: FetchLike = () => Promise.reject(new Error('network down'))
    const client = new DimesClient({ ...baseConfig, transport })
    client.captureFeedback('hi')
    await expect(client.flush()).resolves.toBeUndefined()
  })

  it('flush awaits in-flight deliveries', async () => {
    const seen = vi.fn()
    const transport: FetchLike = async () => {
      await new Promise((r) => setTimeout(r, 10))
      seen()
      return { ok: true, status: 200 }
    }
    const client = new DimesClient({ ...baseConfig, transport })
    client.captureFeedback('hi')
    expect(seen).not.toHaveBeenCalled()
    await client.flush()
    expect(seen).toHaveBeenCalledOnce()
  })
})
