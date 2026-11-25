import { describe, expect, it } from 'vitest'
import { playedCardGainsBlock } from './BattleScreen'

const step = (kind: string, casterInstanceId: string | null, amount: number | null = null) => ({
  event: { kind, casterInstanceId, amount },
})

describe('playedCardGainsBlock', () => {
  it('is true when the caster gains block while the played card resolves', () => {
    const steps = [step('PlayCard', 'hero'), step('GainBlock', 'hero', 5)]
    expect(playedCardGainsBlock(steps, 0, 'hero')).toBe(true)
  })

  it('stops at the next card play', () => {
    const steps = [step('PlayCard', 'hero'), step('Draw', 'hero'), step('PlayCard', 'hero'), step('GainBlock', 'hero', 5)]
    expect(playedCardGainsBlock(steps, 0, 'hero')).toBe(false)
  })

  it('ignores block gained by someone else and zero-amount block', () => {
    const steps = [step('PlayCard', 'hero'), step('GainBlock', 'enemy', 5), step('GainBlock', 'hero', 0)]
    expect(playedCardGainsBlock(steps, 0, 'hero')).toBe(false)
  })
})
