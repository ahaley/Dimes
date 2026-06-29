import type { ChangeKind, ChangeStatus } from './api/types'

// Mirror of the server's transition map — for rendering action affordances only. The API remains the
// source of truth and will reject anything illegal (the UI surfaces its 403/409 error).
export const ALLOWED_TRANSITIONS: Record<ChangeStatus, ChangeStatus[]> = {
  Captured: ['Triaged', 'Approved', 'Rejected', 'Duplicate'],
  Triaged: ['Approved', 'Rejected', 'Duplicate'],
  Approved: ['InDevelopment', 'Rejected', 'Duplicate'],
  InDevelopment: ['InReview', 'Approved', 'Rejected', 'Duplicate'],
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

/** Badge tone for a change Kind. Epics get a distinct tone so the composite stands out on the board;
 * every other kind keeps the neutral slate badge. */
export function kindTone(kind: ChangeKind): string {
  return kind === 'Epic' ? 'indigo' : 'slate'
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

// A fixed, dark-mode-aware palette so each project gets a stable, distinguishable monogram color.
const PROJECT_COLORS = [
  'bg-indigo-100 text-indigo-700 dark:bg-indigo-500/25 dark:text-indigo-200',
  'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/25 dark:text-emerald-200',
  'bg-amber-100 text-amber-800 dark:bg-amber-500/25 dark:text-amber-200',
  'bg-rose-100 text-rose-700 dark:bg-rose-500/25 dark:text-rose-200',
  'bg-sky-100 text-sky-700 dark:bg-sky-500/25 dark:text-sky-200',
  'bg-violet-100 text-violet-700 dark:bg-violet-500/25 dark:text-violet-200',
  'bg-teal-100 text-teal-700 dark:bg-teal-500/25 dark:text-teal-200',
  'bg-orange-100 text-orange-700 dark:bg-orange-500/25 dark:text-orange-200',
]

/** Deterministic Tailwind bg/text classes for a project's monogram, hashed from a stable key (its id). */
export function projectColor(key: string): string {
  let hash = 0
  for (let i = 0; i < key.length; i++) hash = (hash * 31 + key.charCodeAt(i)) | 0
  return PROJECT_COLORS[Math.abs(hash) % PROJECT_COLORS.length]
}
