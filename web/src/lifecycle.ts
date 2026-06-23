import type { ChangeStatus } from './api/types'

// Mirror of the server's transition map — for rendering action affordances only. The API remains the
// source of truth and will reject anything illegal (the UI surfaces its 403/409 error).
export const ALLOWED_TRANSITIONS: Record<ChangeStatus, ChangeStatus[]> = {
  Captured: ['Triaged', 'Approved', 'Rejected', 'Duplicate'],
  Triaged: ['Approved', 'Rejected', 'Duplicate'],
  Approved: ['InDevelopment', 'Rejected', 'Duplicate'],
  InDevelopment: ['InReview', 'Rejected', 'Duplicate'],
  InReview: ['Done', 'InDevelopment', 'Rejected', 'Duplicate'],
  Done: ['InDevelopment'],
  Rejected: [],
  Duplicate: [],
}

export const STATUS_TONE: Record<ChangeStatus, string> = {
  Captured: 'slate',
  Triaged: 'amber',
  Approved: 'violet',
  InDevelopment: 'indigo',
  InReview: 'indigo',
  Done: 'green',
  Rejected: 'red',
  Duplicate: 'red',
}

/** Compact relative age, e.g. "just now", "5m", "3h", "2d", "4w". */
export function relativeTime(iso: string): string {
  const diffMs = Date.now() - new Date(iso).getTime()
  const sec = Math.round(diffMs / 1000)
  if (sec < 60) return 'just now'
  const min = Math.round(sec / 60)
  if (min < 60) return `${min}m`
  const hr = Math.round(min / 60)
  if (hr < 24) return `${hr}h`
  const day = Math.round(hr / 24)
  if (day < 7) return `${day}d`
  const wk = Math.round(day / 7)
  if (wk < 5) return `${wk}w`
  const mo = Math.round(day / 30)
  if (mo < 12) return `${mo}mo`
  return `${Math.round(day / 365)}y`
}

/** Up-to-two-letter initials for an avatar chip. */
export function initials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean)
  if (parts.length === 0) return '?'
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase()
  return (parts[0][0] + parts[parts.length - 1][0]).toUpperCase()
}
