import type { Breadcrumb, ContextMetadata, UserContext } from './types'

/** Collects the context metadata attached to every observation: route, app version, identity,
 * device, and a rolling breadcrumb trail. Degrades gracefully when there is no DOM (SSR). */
export class ContextTracker {
  private user: UserContext = {}
  private crumbs: Breadcrumb[] = []

  constructor(private cfg: { appVersion?: string; release?: string; maxBreadcrumbs?: number }) {}

  identify(user: UserContext): void {
    this.user = { ...this.user, ...user }
  }

  addBreadcrumb(type: string, message: string): void {
    this.crumbs.push({ t: Date.now(), type, message })
    const max = this.cfg.maxBreadcrumbs ?? 20
    if (this.crumbs.length > max) this.crumbs.splice(0, this.crumbs.length - max)
  }

  build(): ContextMetadata {
    const ctx: ContextMetadata = {
      appVersion: this.cfg.appVersion,
      release: this.cfg.release,
    }

    if (typeof window !== 'undefined') {
      if (window.location) ctx.route = window.location.pathname + window.location.hash
      ctx.viewport = { w: window.innerWidth, h: window.innerHeight }
      if (window.navigator) ctx.userAgent = window.navigator.userAgent
    }

    if (Object.keys(this.user).length > 0) ctx.user = { ...this.user }
    if (this.crumbs.length > 0) ctx.breadcrumbs = this.crumbs.slice()
    return ctx
  }
}
