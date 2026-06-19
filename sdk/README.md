# @dimes/sdk

Embeddable, dependency-free, framework-agnostic capture SDK for [Dimes](../specs/spec.md).
It posts **observations** to a configured Dimes ingest source: explicit user feedback, uncaught
errors (with stable fingerprints for server-side aggregation), and rage-click friction — each
enriched with context metadata (route, app version, identity, breadcrumbs, viewport).

## Install

```bash
npm install @dimes/sdk
```

```ts
import { init } from '@dimes/sdk'

const dimes = init({
  endpoint: 'https://dimes.internal',   // base URL of the Dimes API
  sourceId: '<observation-source-id>',  // created under a project in Dimes
  appVersion: '1.4.2',
  sampleRate: 1.0,                       // 0..1 for passive signals (feedback always sends)
  // captureErrors / captureRageClicks default to true
  beforeSend: (e) => e,                  // inspect / redact / return null to drop
})

dimes.identify({ userId: 'u_123', role: 'admin', segment: 'beta' })
dimes.captureFeedback('The export button does nothing')   // explicit feedback
```

### Script tag (no bundler)

```html
<script src="https://unpkg.com/@dimes/sdk/dist/index.global.js"></script>
<script>
  const dimes = Dimes.init({ endpoint: 'https://dimes.internal', sourceId: '...' })
  Dimes.mountFeedbackWidget(dimes)   // optional floating feedback button
</script>
```

## What it captures (pass-1)

| Signal | Kind | How |
|---|---|---|
| Explicit feedback | `ExplicitFeedback` | `captureFeedback(message)` or the optional widget |
| Uncaught errors / rejections | `TechnicalError` | auto (`captureErrors`), fingerprinted by name+message+frame |
| Rage clicks | `BehavioralFriction` | auto (`captureRageClicks`) |
| Anything custom | your choice | `capture({ kind, payload, fingerprint })` |

Context metadata (route, app version, identity, breadcrumbs, viewport, user agent) is attached to
every observation. Nothing is captured when `enabled: false` (e.g. before consent).

## Behavior & privacy

- **Fire-and-forget, serialized**: deliveries never throw into the host app and go out one at a
  time (no request stampede; repeated same-fingerprint signals aggregate server-side).
- **`flush()`** awaits queued deliveries (e.g. before navigation or in tests).
- **`beforeSend`** lets you redact PII or drop events; passive signals honor `sampleRate`.
- **`shutdown()`** removes listeners and flushes.

## Development

```bash
npm install
npm test          # vitest + happy-dom
npm run build     # tsup → dist (ESM, CJS, IIFE, .d.ts)
```
