import { DimesClient } from './client'
import { installErrorCapture } from './integrations'
import { installRageClicks } from './friction'
import type { DimesConfig } from './types'

/** Initialize the SDK: create a client and install the configured passive integrations
 * (error capture, rage-click friction, navigation breadcrumbs). Returns the client. */
export function init(config: DimesConfig): DimesClient {
  const client = new DimesClient(config)

  if (config.captureErrors ?? true) {
    client.registerTeardown(installErrorCapture(client))
  }
  if (config.captureRageClicks ?? true) {
    client.registerTeardown(installRageClicks(client))
  }

  if (typeof window !== 'undefined') {
    const onNavigate = () => client.addBreadcrumb('navigate', window.location.pathname + window.location.hash)
    window.addEventListener('popstate', onNavigate)
    window.addEventListener('hashchange', onNavigate)
    client.registerTeardown(() => {
      window.removeEventListener('popstate', onNavigate)
      window.removeEventListener('hashchange', onNavigate)
    })
  }

  return client
}

export { DimesClient } from './client'
export { mountFeedbackWidget } from './widget'
export { installErrorCapture, errorFingerprint, errorPayload } from './integrations'
export { installRageClicks, describeTarget } from './friction'
export { ContextTracker } from './context'
export type * from './types'
