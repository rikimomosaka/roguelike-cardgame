import { describe, expect, it } from 'vitest'
import type { BattleEventDto, BattleEventStepDto } from '../../api/types'
import { hasDrawPresentationTrigger, hasShufflePresentationTrigger } from './drawPresentation'

function step(
  kind: BattleEventDto['kind'],
  amount: number | null = null,
  targetInstanceId: string | null = 'hero-1',
): BattleEventStepDto {
  return {
    event: {
      kind,
      order: 0,
      casterInstanceId: null,
      targetInstanceId,
      amount,
      cardId: null,
      note: null,
      blockedAmount: null,
      hitIndex: null,
    },
    snapshotAfter: {} as BattleEventStepDto['snapshotAfter'],
  }
}

describe('drawPresentation', () => {
  it('does not treat a play, recovery or hand rerender as a draw', () => {
    expect(hasDrawPresentationTrigger([step('PlayCard')], 'hero-1')).toBe(false)
    expect(hasDrawPresentationTrigger([step('RecoverFromDiscard', 1)], 'hero-1')).toBe(false)
    expect(hasDrawPresentationTrigger([], 'hero-1')).toBe(false)
  })

  it('accepts an explicit draw and the legacy base TurnStart draw', () => {
    expect(hasDrawPresentationTrigger([step('Draw', 1)], 'hero-1')).toBe(true)
    expect(hasDrawPresentationTrigger([step('TurnStart')], 'hero-1')).toBe(true)
    expect(hasDrawPresentationTrigger([step('Draw', 0)], 'hero-1')).toBe(false)
  })

  it('does not stage another participant draw or infer a shuffle from piles', () => {
    expect(hasDrawPresentationTrigger([step('Draw', 1, 'ally-2')], 'hero-1')).toBe(false)
    expect(hasShufflePresentationTrigger([step('Draw', 2)], 'hero-1')).toBe(false)
    expect(hasShufflePresentationTrigger([step('Shuffle', 4)], 'hero-1')).toBe(true)
  })
})
