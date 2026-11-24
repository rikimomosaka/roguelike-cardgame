import { describe, expect, it } from 'vitest'
import type { BattleCardInstanceDto, BattleEventDto, BattleEventStepDto } from '../../api/types'
import { planInlineDraws } from './inlineDrawPlan'

const card = (id: string) => ({ instanceId: id }) as BattleCardInstanceDto

function step(kind: BattleEventDto['kind'], amount: number | null = null): BattleEventStepDto {
  return {
    event: {
      kind,
      order: 0,
      casterInstanceId: 'hero',
      targetInstanceId: 'hero',
      amount,
      cardId: null,
      note: null,
      blockedAmount: null,
      hitIndex: null,
    },
    snapshotAfter: {} as BattleEventStepDto['snapshotAfter'],
  }
}

const ids = (cards: readonly BattleCardInstanceDto[]) => cards.map(c => c.instanceId)

describe('planInlineDraws', () => {
  it('plays each draw at its own event position, in effect order', () => {
    const steps = [step('PlayCard'), step('Draw', 1), step('GainEnergy', 1), step('Draw', 1)]
    const plan = planInlineDraws({
      steps,
      actorInstanceId: 'hero',
      newlyDrawn: [card('a'), card('b')],
      survivingHand: [card('keep')],
      preDrawPile: [card('a'), card('b'), card('c')],
      preDiscardPile: [card('d')],
      reshuffleCards: null,
    })

    const first = plan.actionsByStepIndex.get(1)!
    expect(first).toHaveLength(1)
    expect(first[0].kind).toBe('draw')
    if (first[0].kind !== 'draw') throw new Error()
    expect(ids(first[0].cards)).toEqual(['a'])
    expect(ids(first[0].states[0].hand)).toEqual(['keep', 'a'])
    expect(ids(first[0].states[0].drawPile)).toEqual(['b', 'c'])

    const second = plan.actionsByStepIndex.get(3)!
    if (second[0].kind !== 'draw') throw new Error()
    expect(ids(second[0].cards)).toEqual(['b'])
    expect(ids(second[0].states[0].hand)).toEqual(['keep', 'a', 'b'])
    expect(plan.trailing).toEqual([])
  })

  it('draws from the old pile, reshuffles, then draws the rest at the Shuffle/Draw pair', () => {
    const steps = [step('PlayCard'), step('Shuffle', 3), step('Draw', 3)]
    const plan = planInlineDraws({
      steps,
      actorInstanceId: 'hero',
      newlyDrawn: [card('old'), card('r1'), card('r2')],
      survivingHand: [],
      preDrawPile: [card('old')],
      preDiscardPile: [card('r1'), card('r2'), card('r3')],
      reshuffleCards: [card('r3'), card('r1'), card('r2')],
    })

    const atShuffle = plan.actionsByStepIndex.get(1)!
    expect(atShuffle.map(action => action.kind)).toEqual(['draw', 'reshuffle'])
    if (atShuffle[0].kind !== 'draw' || atShuffle[1].kind !== 'reshuffle') throw new Error()
    expect(ids(atShuffle[0].cards)).toEqual(['old'])
    expect(ids(atShuffle[1].stateAfter.discardPile)).toEqual([])

    const atDraw = plan.actionsByStepIndex.get(2)!
    if (atDraw[0].kind !== 'draw') throw new Error()
    expect(ids(atDraw[0].cards)).toEqual(['r1', 'r2'])
    expect(ids(atDraw[0].states[1].hand)).toEqual(['old', 'r1', 'r2'])
    expect(ids(atDraw[0].states[1].drawPile)).toEqual(['r3'])
  })

  it('ignores another actor and keeps unmatched draws as trailing', () => {
    const other = { ...step('Draw', 1), event: { ...step('Draw', 1).event, targetInstanceId: 'ally' } }
    const plan = planInlineDraws({
      steps: [other],
      actorInstanceId: 'hero',
      newlyDrawn: [card('a')],
      survivingHand: [],
      preDrawPile: [card('a')],
      preDiscardPile: [],
      reshuffleCards: null,
    })
    expect(plan.actionsByStepIndex.size).toBe(0)
    expect(plan.trailing).toHaveLength(1)
  })
})
