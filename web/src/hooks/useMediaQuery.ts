import { useSyncExternalStore } from 'react'

/**
 * Subscribe to a CSS media query, re-rendering when it starts/stops matching. Uses
 * useSyncExternalStore so the value stays consistent with React's render. This is a client-only
 * SPA; the server snapshot (never used) defaults to false.
 */
export function useMediaQuery(query: string): boolean {
  return useSyncExternalStore(
    (onStoreChange) => {
      const mql = window.matchMedia(query)
      mql.addEventListener('change', onStoreChange)
      return () => mql.removeEventListener('change', onStoreChange)
    },
    () => window.matchMedia(query).matches,
    () => false,
  )
}

/**
 * True at Tailwind's `md` breakpoint and up (≥768px) — the app's desktop/mobile cutoff. Matches the
 * sidebar drawer-vs-rail switch and the board's one-stage-vs-columns switch, so layouts agree.
 */
export function useIsDesktop(): boolean {
  return useMediaQuery('(min-width: 768px)')
}
