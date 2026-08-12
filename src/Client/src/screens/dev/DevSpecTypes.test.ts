import { describe, expect, it } from 'vitest'
import { parseSpec, specToJsonObject } from './DevSpecTypes'

describe('modifyCardCost dev spec round-trip', () => {
  it('preserves the target count and modifier duration', () => {
    const spec = parseSpec(JSON.stringify({
      rarity: 1,
      cardType: 'Skill',
      cost: 1,
      effects: [{
        action: 'modifyCardCost',
        scope: 'self',
        amount: -2,
        pile: 'draw',
        select: 'random',
        cardCount: 3,
        costDuration: 'untilPlayed',
      }],
    }))

    expect(spec.effects[0]).toMatchObject({
      cardCount: 3,
      costDuration: 'untilPlayed',
    })
    expect(specToJsonObject(spec)).toMatchObject({
      effects: [expect.objectContaining({
        cardCount: 3,
        costDuration: 'untilPlayed',
      })],
    })
  })
})
