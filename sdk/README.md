# dimes-capture-sdk

Embeddable, dependency-free, framework-agnostic capture SDK for [Dimes](../specs/spec.md).
It posts **observations** to a configured Dimes ingest source: explicit user feedback, uncaught
errors (with stable fingerprints for server-side aggregation), and rage-click friction — each
enriched with context metadata (route, app version, identity, breadcrumbs, viewport).

## Install

```bash
npm install dimes-capture-sdk
```

```ts
import { init } from 'dimes-capture-sdk'

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
<script src="https://unpkg.com/dimes-capture-sdk/dist/index.global.js"></script>
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

## Angular

The SDK is framework-agnostic, so it drops into Angular as a small service. Only two pieces need
Angular-specific glue (router breadcrumbs and the error handler); everything else works unchanged.

> **Cross-origin note.** Dimes expects the SDK to post **same-origin** (its own SPA and API sit behind
> one reverse proxy) and ships no CORS headers. The SDK sends `application/json`, which triggers a CORS
> preflight, so if your app runs on a different origin than the Dimes API the browser will block
> delivery. Either proxy a path on your app's origin through to the Dimes API and point `endpoint` at
> it, or enable CORS on Dimes for your origin.

**1. Create an Observation Source** of type `Sdk` in the project's settings and copy its **id** (a
GUID). That GUID is your `sourceId` — the source's identifier, not the name you gave it.

### Example: an Angular service

Wrap the client in an injectable service and initialize it once, in the browser:

```ts
// dimes.service.ts
import { Injectable } from '@angular/core'
import { init, type DimesClient } from 'dimes-capture-sdk'
import { environment } from '../environments/environment'

@Injectable({ providedIn: 'root' })
export class DimesService {
  private client?: DimesClient

  /** Call once. The SDK no-ops under SSR, but guard on `window` anyway. */
  start(): void {
    if (this.client || typeof window === 'undefined') return
    this.client = init({
      endpoint: environment.dimesEndpoint, // same-origin base that proxies to the Dimes API
      sourceId: environment.dimesSourceId,  // the Observation Source GUID
      appVersion: environment.appVersion,
    })
  }

  get instance(): DimesClient | undefined {
    return this.client
  }

  identify(user: { userId?: string; role?: string; segment?: string }): void {
    this.client?.identify(user)
  }

  feedback(message: string): void {
    this.client?.captureFeedback(message)
  }
}
```

Then start it at bootstrap and add the two Angular bridges (standalone `app.config.ts`):

```ts
// app.config.ts
import { ApplicationConfig, APP_INITIALIZER, ErrorHandler, inject } from '@angular/core'
import { Router, NavigationEnd } from '@angular/router'
import { filter } from 'rxjs'
import { DimesService } from './dimes.service'

/** Forward Angular-handled exceptions, which never reach window.onerror. */
class DimesErrorHandler implements ErrorHandler {
  private dimes = inject(DimesService)
  handleError(error: unknown): void {
    const e = error instanceof Error ? error : new Error(String(error))
    this.dimes.instance?.capture({
      kind: 'TechnicalError',
      payload: { name: e.name, message: e.message, stack: e.stack?.split('\n').slice(0, 5).join('\n') },
      fingerprint: `err:${e.name}:${e.message}`,
    })
    console.error(error) // keep Angular's default logging
  }
}

export const appConfig: ApplicationConfig = {
  providers: [
    { provide: ErrorHandler, useClass: DimesErrorHandler },
    {
      // On Angular 19+, `provideAppInitializer(() => { ... })` is the newer equivalent.
      provide: APP_INITIALIZER,
      multi: true,
      useFactory: () => {
        const dimes = inject(DimesService)
        const router = inject(Router)
        return () => {
          dimes.start()
          router.events
            .pipe(filter((e): e is NavigationEnd => e instanceof NavigationEnd))
            .subscribe((e) => dimes.instance?.addBreadcrumb('navigate', e.urlAfterRedirects))
        }
      },
    },
  ],
}
```

**Why those two bridges** (the rest of the SDK needs no Angular wiring):

- **Router breadcrumbs** — the SDK auto-listens to `popstate` / `hashchange`, but Angular Router
  navigations use `history.pushState`, which doesn't fire `popstate`; the `NavigationEnd` subscription
  records in-app route changes.
- **`ErrorHandler`** — the SDK hooks `window` `error` / `unhandledrejection`, but Angular routes
  exceptions through its own `ErrorHandler`, so forwarding from a custom handler gives full coverage.

**Using it from components:** inject `DimesService` and call `identify(...)` after login,
`feedback('…')` from a control, or `mountFeedbackWidget(dimes.instance!, { label: 'Send feedback' })`
for the built-in floating button. Passive error and rage-click capture run on their own.

## Development

```bash
npm install
npm test          # vitest + happy-dom
npm run build     # tsup → dist (ESM, CJS, IIFE, .d.ts)
```
