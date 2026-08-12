import { describe, expect, it } from 'vitest'
import {
  formatAttackIntentSummary,
  formatAttackScopeLabel,
  toAttackIntentSummarySegments,
} from './intentSummary'

describe('attack intent summary', () => {
  it('formats the total first with no spaces as X(NHIT)', () => {
    expect(formatAttackIntentSummary({ total: 18, hits: 3 })).toBe('18(3HIT)')
  })

  it('uses compact uppercase labels for every attack scope', () => {
    expect(formatAttackScopeLabel('single')).toBe('SINGLE')
    expect(formatAttackScopeLabel('random')).toBe('RANDOM')
    expect(formatAttackScopeLabel('all')).toBe('ALL')
  })

  it('uses each scope hit count and keeps single/random/all in a stable order', () => {
    const segments = toAttackIntentSummarySegments({
      single: 12,
      random: 8,
      all: 4,
      hits: 9,
      singleHits: 2,
      randomHits: 3,
      allHits: 4,
    })

    expect(segments).toEqual([
      { total: 12, hits: 2, scope: 'single' },
      { total: 8, hits: 3, scope: 'random' },
      { total: 4, hits: 4, scope: 'all' },
    ])
    expect(segments.map(formatAttackIntentSummary)).toEqual([
      '12(2HIT)',
      '8(3HIT)',
      '4(4HIT)',
    ])
  })

  it('falls back to the shared hit count and still renders zero totals', () => {
    expect(toAttackIntentSummarySegments({ single: 0, hits: 2 })).toEqual([
      { total: 0, hits: 2, scope: 'single' },
    ])
    expect(toAttackIntentSummarySegments({ all: 5 })).toEqual([
      { total: 5, hits: 1, scope: 'all' },
    ])
  })
})
