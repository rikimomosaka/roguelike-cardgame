export type AttackIntentScope = 'single' | 'random' | 'all'

export type AttackIntentBreakdown = Readonly<{
  single?: number
  random?: number
  all?: number
  hits?: number
  singleHits?: number
  randomHits?: number
  allHits?: number
}>

export type AttackIntentSummarySegment = Readonly<{
  total: number
  hits: number
  scope: AttackIntentScope
}>

/**
 * Keep the display contract independent from React so every attack scope uses
 * the compact `X(NHIT)` notation in the same, deterministic order.
 */
export function toAttackIntentSummarySegments(
  attack: AttackIntentBreakdown,
): AttackIntentSummarySegment[] {
  const segments: AttackIntentSummarySegment[] = []

  if (attack.single !== undefined) {
    segments.push({
      total: attack.single,
      hits: attack.singleHits ?? attack.hits ?? 1,
      scope: 'single',
    })
  }
  if (attack.random !== undefined) {
    segments.push({
      total: attack.random,
      hits: attack.randomHits ?? attack.hits ?? 1,
      scope: 'random',
    })
  }
  if (attack.all !== undefined) {
    segments.push({
      total: attack.all,
      hits: attack.allHits ?? attack.hits ?? 1,
      scope: 'all',
    })
  }

  return segments
}

export function formatAttackIntentSummary(
  segment: Pick<AttackIntentSummarySegment, 'total' | 'hits'>,
): string {
  return `${segment.total}(${segment.hits}HIT)`
}

export function formatAttackScopeLabel(scope: AttackIntentScope): string {
  switch (scope) {
    case 'single': return 'SINGLE'
    case 'random': return 'RANDOM'
    case 'all': return 'ALL'
  }
}
