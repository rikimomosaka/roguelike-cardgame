import type { CombatFeedbackItem, CombatFeedbackKind } from './CombatFeedbackLayer'

const DEFAULT_FEEDBACK_LIFETIME_MS = 860
export const STATUS_FEEDBACK_LIFETIME_MS = 1500

/**
 * Buff/debuff names are semantic information rather than impact punctuation.
 * Keep them mounted long enough to read after their entrance has completed.
 */
export function combatFeedbackLifetimeMs(kind: CombatFeedbackKind): number {
  return kind === 'buff' || kind === 'debuff' || kind === 'fail'
    ? STATUS_FEEDBACK_LIFETIME_MS
    : DEFAULT_FEEDBACK_LIFETIME_MS
}

export function blockGainFeedbackText(amount: number): string {
  return `ブロック +${amount}`
}

function isStatusFeedback(item: CombatFeedbackItem): boolean {
  return item.kind === 'buff' || item.kind === 'debuff' || item.kind === 'fail'
}

/**
 * Impact numbers are deliberately capped, but status names stay mounted until
 * their own 1.5s timer expires. A dense multi-hit must never evict semantic
 * buff/debuff feedback before it has been readable for roughly one second.
 */
export function appendCombatFeedback(
  current: readonly CombatFeedbackItem[],
  next: CombatFeedbackItem,
): CombatFeedbackItem[] {
  const statuses = current.filter(isStatusFeedback)
  const impacts = current.filter(item => !isStatusFeedback(item))

  if (isStatusFeedback(next)) {
    return [...statuses, next, ...impacts].sort((left, right) => left.id - right.id)
  }

  const retainedImpacts = impacts
    .filter(item => item.targetId !== next.targetId || item.id > next.id - 6)
  const cappedImpacts = [...retainedImpacts, next].slice(-32)
  return [...statuses, ...cappedImpacts].sort((left, right) => left.id - right.id)
}
