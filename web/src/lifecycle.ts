import type { ChangeStatus } from './api/types'

// Mirror of the server's transition map — for rendering action affordances only. The API remains the
// source of truth and will reject anything illegal (the UI surfaces its 403/409 error).
export const ALLOWED_TRANSITIONS: Record<ChangeStatus, ChangeStatus[]> = {
  Captured: ['Triaged', 'Rejected', 'Duplicate'],
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
