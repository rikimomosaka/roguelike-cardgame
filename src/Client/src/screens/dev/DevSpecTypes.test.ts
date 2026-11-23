import { describe, expect, it } from 'vitest'
import { parseSpec, specToJsonObject } from './DevSpecTypes'

describe('modifyCardCost dev spec round-trip', () => {
  it('preserves grouped statuses and omits ambiguous legacy fields', () => {
    const spec = parseSpec(JSON.stringify({
      rarity: 1,
      cardType: 'Skill',
      cost: 1,
      comboGaugeCost: 6,
      effects: [{
        action: 'debuff',
        scope: 'self',
        statusChanges: [
          { name: 'silence', amount: 1 },
          { name: 'handCostIncrease', amount: 2 },
        ],
      }],
    }))

    const effect = specToJsonObject(spec).effects as Record<string, unknown>[]
    expect(effect[0]).toEqual({
      action: 'debuff',
      scope: 'self',
      statusChanges: [
        { name: 'silence', amount: 1 },
        { name: 'handCostIncrease', amount: 2 },
      ],
    })
  })

  it('preserves the target count and modifier duration', () => {
    const spec = parseSpec(JSON.stringify({
      rarity: 1,
      cardType: 'Skill',
      cost: 1,
      comboGaugeCost: 6,
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

  it('keeps normal/upgraded keywords and usage limits as independent card fields', () => {
    const spec = parseSpec(JSON.stringify({
      rarity: 1,
      cardType: 'Skill',
      cost: 1,
      effects: [],
      keywords: ['wild'],
      upgradedKeywords: [],
      usesPerTurn: 1,
      upgradedUsesPerTurn: 3,
    }))

    expect(spec.keywords).toEqual(['wild'])
    expect(spec.upgradedKeywords).toEqual([])
    expect(spec.usesPerTurn).toBe(1)
    expect(spec.upgradedUsesPerTurn).toBe(3)
    expect(specToJsonObject(spec)).toMatchObject({
      keywords: ['wild'],
      upgradedKeywords: [],
      usesPerTurn: 1,
      upgradedUsesPerTurn: 3,
    })
  })

  it('round-trips momentum and typed card abilities without string encoding', () => {
    const input = {
      rarity: 2,
      cardType: 'Skill',
      cost: 1,
      comboGaugeCost: 6,
      effects: [{
        action: 'attack', scope: 'single', side: 'enemy', amount: 11, maxComboMin: 3,
      }],
      abilities: [{
        type: 'reprise',
        filter: { cardTypes: ['Skill'], maxOriginalCost: 3, requiredKeywords: ['wild'] },
      }],
      upgradedAbilities: [{
        type: 'contingency',
        trigger: { timing: 'ComboReached', comboThreshold: 4 },
      }],
    }

    const output = specToJsonObject(parseSpec(JSON.stringify(input)))

    expect(output.comboGaugeCost).toBe(6)
    expect(output).toMatchObject(input)
  })

  it('preserves unknown siblings when known fields are edited', () => {
    const parsed = parseSpec(JSON.stringify({
      rarity: 1,
      cardType: 'Skill',
      cost: 1,
      futureRoot: { mode: 'root' },
      effects: [{
        action: 'attack', scope: 'single', side: 'enemy', amount: 5,
        futureEffect: { mode: 'effect' },
      }],
      abilities: [{
        type: 'reprise',
        futureAbility: { mode: 'ability' },
        filter: {
          cardTypes: ['Skill'], maxOriginalCost: 3,
          futureFilter: { mode: 'filter' },
        },
      }],
      upgradedAbilities: [{
        type: 'contingency',
        trigger: {
          timing: 'ComboReached', comboThreshold: 4,
          futureTrigger: { mode: 'trigger' },
        },
      }],
    }))

    const edited = {
      ...parsed,
      cost: 2,
      effects: [{ ...parsed.effects[0], amount: 8 }],
    }
    const output = specToJsonObject(edited)

    expect(output).toMatchObject({
      cost: 2,
      futureRoot: { mode: 'root' },
      effects: [{ amount: 8, futureEffect: { mode: 'effect' } }],
      abilities: [{
        type: 'reprise', futureAbility: { mode: 'ability' },
        filter: {
          cardTypes: ['Skill'], maxOriginalCost: 3,
          futureFilter: { mode: 'filter' },
        },
      }],
      upgradedAbilities: [{
        type: 'contingency',
        trigger: {
          timing: 'ComboReached', comboThreshold: 4,
          futureTrigger: { mode: 'trigger' },
        },
      }],
    })
  })
})
