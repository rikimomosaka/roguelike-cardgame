import { describe, expect, it } from 'vitest'
import type { BattleCardInstanceDto, CombatActorDto, IntentDto } from '../../api/types'
import type { CardCatalog, CharacterCatalog, EnemyCatalog, UnitCatalog } from '../../api/catalog'
import {
  toBuffs,
  toCharacterDemo,
  toHandCardDemo,
  toIntentDemos,
} from './dtoAdapter'

function heroActor(): CombatActorDto {
  return {
    instanceId: 'hero_inst',
    definitionId: 'hero',
    side: 'Ally',
    slotIndex: 0,
    currentHp: 80,
    maxHp: 80,
    blockDisplay: 0,
    statuses: {},
    intent: null,
  } as unknown as CombatActorDto
}

function enemyActor(definitionId: string): CombatActorDto {
  return {
    instanceId: `${definitionId}_inst`,
    definitionId,
    side: 'Enemy',
    slotIndex: 0,
    currentHp: 10,
    maxHp: 10,
    blockDisplay: 0,
    statuses: {},
    intent: null,
  } as unknown as CombatActorDto
}

function summonActor(definitionId: string): CombatActorDto {
  return {
    instanceId: `${definitionId}_inst`,
    definitionId,
    side: 'Ally',
    slotIndex: 1,
    currentHp: 30,
    maxHp: 30,
    blockDisplay: 0,
    statuses: {},
    intent: null,
  } as unknown as CombatActorDto
}

const characterCatalog: CharacterCatalog = {
  default: {
    id: 'default', name: '見習い冒険者',
    maxHp: 80, startingGold: 99, potionSlotCount: 3, heightTier: 7,
  },
}

const enemyCatalog: EnemyCatalog = {
  dire_wolf: {
    id: 'dire_wolf', name: 'ダイア・ウルフ', imageId: 'wolf_dire',
    hp: 40, initialMoveId: 'howl', heightTier: 6,
  },
}

const unitCatalog: UnitCatalog = {
  wisp: {
    id: 'wisp', name: 'ウィスプ', imageId: 'wisp',
    hp: 30, initialMoveId: 'wisp_strike', lifetimeTurns: 3, heightTier: 3,
  },
}

describe('toCharacterDemo', () => {
  it('hero name uses accountId (not catalog name)', () => {
    const demo = toCharacterDemo(heroActor(), {
      enemies: enemyCatalog, units: unitCatalog, characters: characterCatalog,
    }, 'alice')
    expect(demo.name).toBe('alice')
    expect(demo.name).not.toBe('見習い冒険者')   // catalog name must NOT win
    expect(demo.spriteKind).toBe('hero')
    expect(demo.image).toBe('/characters/player_stand.png')
    expect(demo.animations).toEqual({
      idle: {
        src: '/characters/player_idle.png?v=2',
        frameWidth: 128,
        frameHeight: 256,
        frameCount: 8,
        durationMs: 1200,
        loop: true,
      },
      attack: {
        src: '/characters/player_attack.png?v=1',
        frameWidth: 192,
        frameHeight: 256,
        frameCount: 4,
        durationMs: 240,
        loop: false,
      },
      hurt: {
        src: '/characters/player_hurt.png?v=1',
        frameWidth: 192,
        frameHeight: 256,
        frameCount: 4,
        durationMs: 300,
        loop: false,
      },
    })
  })

  it('hero heightTier comes from character catalog (looked up by default)', () => {
    const demo = toCharacterDemo(heroActor(), {
      enemies: enemyCatalog, units: unitCatalog, characters: characterCatalog,
    }, 'alice')
    expect(demo.heightTier).toBe(7)
  })

  it('hero falls back to heightTier=5 if character catalog is null', () => {
    const demo = toCharacterDemo(heroActor(), {
      enemies: enemyCatalog, units: unitCatalog, characters: null,
    }, 'alice')
    expect(demo.heightTier).toBe(5)
  })

  it('enemy heightTier comes from enemy catalog', () => {
    const demo = toCharacterDemo(enemyActor('dire_wolf'), {
      enemies: enemyCatalog, units: unitCatalog, characters: characterCatalog,
    }, 'alice')
    expect(demo.heightTier).toBe(6)
    expect(demo.name).toBe('ダイア・ウルフ')
    expect(demo.animations).toBeUndefined()
  })

  it('summon heightTier comes from unit catalog', () => {
    const demo = toCharacterDemo(summonActor('wisp'), {
      enemies: enemyCatalog, units: unitCatalog, characters: characterCatalog,
    }, 'alice')
    expect(demo.heightTier).toBe(3)
    expect(demo.name).toBe('ウィスプ')
  })

  it('enemy with missing catalog entry falls back to heightTier=5', () => {
    const demo = toCharacterDemo(enemyActor('unknown'), {
      enemies: {}, units: unitCatalog, characters: characterCatalog,
    }, 'alice')
    expect(demo.heightTier).toBe(5)
  })
})

describe('toBuffs', () => {
  it('maps the per-hit defensive and retaliation statuses to their PNG icons', () => {
    const actor = {
      ...heroActor(),
      statuses: { barrier: 2, lifesteal: 25, reflect: 3 },
    }

    expect(toBuffs(actor)).toEqual(expect.arrayContaining([
      expect.objectContaining({ name: 'バリア', icon: '/icons/ui/barrier.png', num: 2 }),
      expect.objectContaining({ name: '吸血', icon: '/icons/ui/lifesteal.png', num: 25 }),
      expect.objectContaining({ name: '反射', icon: '/icons/ui/reflect.png', num: 3 }),
    ]))
  })

  it('maps hand-wide cost reduction and increase as a buff and debuff', () => {
    const actor = {
      ...heroActor(),
      statuses: { handCostReduction: 2, handCostIncrease: 1 },
    }

    expect(toBuffs(actor)).toEqual(expect.arrayContaining([
      expect.objectContaining({ kind: 'buff', name: '手札軽減', num: 2 }),
      expect.objectContaining({ kind: 'debuff', name: '手札増加', num: 1 }),
    ]))
  })
})

describe('toIntentDemos', () => {
  function attackIntent(overrides: Partial<IntentDto>): IntentDto {
    return {
      attackSingle: null,
      attackRandom: null,
      attackAll: null,
      attackHits: null,
      attackSingleHits: null,
      attackRandomHits: null,
      attackAllHits: null,
      block: null,
      hasBuff: false,
      hasDebuff: false,
      hasHeal: false,
      ...overrides,
    }
  }

  it('states the real hit count and the summed damage without implying total per hit', () => {
    const [demo] = toIntentDemos(attackIntent({
      attackSingle: 5,
      attackRandom: 3,
      attackHits: 2,
      attackSingleHits: 1,
      attackRandomHits: 1,
    }))

    expect(demo.desc).toContain('単体攻撃：1 HIT、合計5ダメージ')
    expect(demo.desc).toContain('ランダム攻撃：1 HIT、合計3ダメージ')
    expect(demo.attack).toMatchObject({
      single: 5,
      random: 3,
      hits: 2,
      singleHits: 1,
      randomHits: 1,
      total: 8,
    })
  })

  it('keeps a zero-damage hit visible for on-hit effects', () => {
    const [demo] = toIntentDemos(attackIntent({
      attackSingle: 0,
      attackHits: 1,
      attackSingleHits: 1,
    }))

    expect(demo.desc).toContain('単体攻撃：1 HIT、合計0ダメージ')
    expect(demo.attack).toMatchObject({
      single: 0,
      hits: 1,
      singleHits: 1,
      total: 0,
    })
  })

  it('uses the All icon and breakdown supplied for Omnistrike', () => {
    const [demo] = toIntentDemos(attackIntent({
      attackAll: 13,
      attackHits: 3,
      attackAllHits: 3,
    }))

    expect(demo.icon).toBe('/icons/ui/attack_all.png')
    expect(demo.attack).toMatchObject({ all: 13, hits: 3, allHits: 3, total: 13 })
    expect(demo.attack?.single).toBeUndefined()
    expect(demo.attack?.random).toBeUndefined()
  })
})

function battleCard(
  cardDefinitionId: string,
  originalCost: number | null,
  currentCost: number | null = originalCost,
  isUpgraded = false,
): BattleCardInstanceDto {
  return {
    instanceId: `${cardDefinitionId}_instance`,
    cardDefinitionId,
    isUpgraded,
    originalCost,
    currentCost,
    costOverride: null,
    adjustedDescription: null,
    adjustedUpgradedDescription: null,
  }
}

const comboCatalog: CardCatalog = {
  normal_0: {
    id: 'normal_0', name: 'Free', displayName: null, rarity: 0,
    cardType: 'Skill', cost: 0, upgradedCost: null, upgradable: false,
    description: '', upgradedDescription: null,
  },
  normal_5: {
    id: 'normal_5', name: 'Normal', displayName: null, rarity: 0,
    cardType: 'Attack', cost: 5, upgradedCost: null, upgradable: false,
    description: '', upgradedDescription: null,
  },
  wild_5: {
    id: 'wild_5', name: 'Wild', displayName: null, rarity: 0,
    cardType: 'Attack', cost: 5, upgradedCost: null, upgradable: false,
    description: '', upgradedDescription: null,
  },
  upgraded_superwild_5: {
    id: 'upgraded_superwild_5', name: 'SuperWild+', displayName: null, rarity: 0,
    cardType: 'Skill', cost: 4, upgradedCost: 5, upgradable: true,
    description: '', upgradedDescription: '',
  },
}

describe('combo cost presentation', () => {
  it('does not show a redundant original-cost circle when zero clamps to zero', () => {
    const demo = toHandCardDemo(battleCard('normal_0', 0), comboCatalog, 0)

    expect(demo.cost).toBe(0)
    expect(demo.costOrig).toBeNull()
    expect(demo.playable).toBe(true)
  })

  it('uses the authoritative current cost and exposes the original combo cost below it', () => {
    const card = battleCard('normal_5', 5, 0)
    const demo = toHandCardDemo(card, comboCatalog, 0)
    expect(demo.cost).toBe(0)
    expect(demo.costOrig).toBe(5)
    expect(demo.playable).toBe(true)
  })

  it('does not recompute combo rules in the client', () => {
    const card = battleCard('wild_5', 5, 4)
    const demo = toHandCardDemo(card, comboCatalog, 4)
    expect(demo.cost).toBe(4)
    expect(demo.costOrig).toBe(5)
    expect(demo.playable).toBe(true)
  })

  it('also shows an increased current cost above the unchanged combo reference', () => {
    const card = battleCard('normal_5', 5, 7)
    expect(toHandCardDemo(card, comboCatalog, 6)).toMatchObject({
      cost: 7,
      costOrig: 5,
      playable: false,
    })
  })

  it('keeps unplayable X cards unplayable', () => {
    expect(toHandCardDemo(
      battleCard('normal_5', null, null), comboCatalog, 99,
    )).toMatchObject({ cost: 'X', costOrig: null, playable: false })
  })
})
