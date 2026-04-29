import { describe, expect, it } from 'vitest'
import type { CombatActorDto } from '../../api/types'
import type { CharacterCatalog, EnemyCatalog, UnitCatalog } from '../../api/catalog'
import { toCharacterDemo } from './dtoAdapter'

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
    maxHp: 80, startingGold: 99, potionSlotCount: 3, heightTier: 5,
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
  it('hero name uses accountId', () => {
    const demo = toCharacterDemo(heroActor(), {
      enemies: enemyCatalog, units: unitCatalog, characters: characterCatalog,
    }, 'alice')
    expect(demo.name).toBe('alice')
    expect(demo.spriteKind).toBe('hero')
  })

  it('hero heightTier comes from character catalog (default=5)', () => {
    const demo = toCharacterDemo(heroActor(), {
      enemies: enemyCatalog, units: unitCatalog, characters: characterCatalog,
    }, 'alice')
    expect(demo.heightTier).toBe(5)
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
