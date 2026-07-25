import type { ProjectQuota } from './api/types'

/**
 * The single source of truth for how a spent project allowance is explained to the user. Two distinct
 * situations, because the remedy differs:
 *
 *  - `limit <= 0` — this site reserves project creation for administrators, so the viewer never had an
 *    allowance to spend. An admin can still grant them one individually.
 *  - `used >= limit` — they had an allowance and used it. Archiving deliberately doesn't free a slot
 *    (the backend counts archived projects), so saying so up front prevents a pointless round trip.
 *
 * Both end at the same out-of-band ask: there is intentionally no in-app request flow, so the copy
 * names the remedy explicitly rather than leaving the user to guess.
 */
export function projectLimitMessage(quota: ProjectQuota): { heading: string; detail: string } {
  if (quota.limit <= 0) {
    return {
      heading: 'Admins create projects',
      detail:
        'This site reserves project creation for administrators. Ask one to set you up with a project, '
        + 'or to grant you a limit of your own.',
    }
  }
  return {
    heading: 'Project limit reached',
    detail:
      `You've created all ${quota.limit} project${quota.limit === 1 ? '' : 's'} you're allowed. `
      + "Archiving one doesn't free a slot — ask a site administrator to raise your limit.",
  }
}
