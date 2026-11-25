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
  guardian_mireille: {
    id: 'guardian_mireille', name: 'ミレイユ',
    maxHp: 96, startingGold: 99, potionSlotCount: 3, heightTier: 6,
    traitName: '積層装甲',
    traitDescription: 'ターン開始時に堅持1。',
    traitEffects: '自身に[S:steadfast][N:1]を付与。',
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
    expect(demo.image).toBe('/assets/actors/heroes/default/stand.png')
    // 攻撃B・奥義・ダウンの追加分は actorVisualManifest.test.ts で確認する。
    expect(demo.animations).toMatchObject({
      idle: {
        src: '/assets/actors/heroes/default/idle.png',
        frameWidth: 128,
        frameHeight: 128,
        frameCount: 8,
        durationMs: 1200,
        loop: true,
      },
      attack: {
        src: '/assets/actors/heroes/default/attack.png',
        frameWidth: 128,
        frameHeight: 128,
        frameCount: 6,
        durationMs: 600,
        loop: false,
      },
      hurt: {
        src: '/assets/actors/heroes/default/hurt.png',
        frameWidth: 128,
        frameHeight: 128,
        frameCount: 4,
        durationMs: 400,
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

  it('uses the owning participant character for height and rich trait text', () => {
    const demo = toCharacterDemo(heroActor(), {
      enemies: enemyCatalog, units: unitCatalog, characters: characterCatalog,
    }, 'alice', 'guardian_mireille')

    expect(demo.heightTier).toBe(6)
    expect(demo.traitName).toBe('積層装甲')
    expect(demo.traitDescription).toBe('自身に[S:steadfast][N:1]を付与。')
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
    expect(demo.image).toBe('/assets/actors/enemies/wolf_dire/stand.png')
    expect(demo.animations).toBeUndefined()
  })

  it('summon heightTier comes from unit catalog', () => {
    const demo = toCharacterDemo(summonActor('wisp'), {
      enemies: enemyCatalog, units: unitCatalog, characters: characterCatalog,
    }, 'alice')
    expect(demo.heightTier).toBe(3)
    expect(demo.name).toBe('ウィスプ')
    expect(demo.image).toBe('/assets/actors/units/wisp/stand.png')
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
      expect.objectContaining({ name: 'バリア', icon: '/assets/icons/status/barrier.png', num: 2 }),
      expect.objectContaining({ name: '吸血', icon: '/assets/icons/status/lifesteal.png', num: 25 }),
      expect.objectContaining({ name: '反射', icon: '/assets/icons/status/reflect.png', num: 3 }),
    ]))
  })

  it('maps hand-wide cost reduction and increase as a buff and debuff', () => {
    const actor = {
      ...heroActor(),
      statuses: { handCostReduction: 2, handCostIncrease: 1 },
    }

    expect(toBuffs(actor)).toEqual(expect.arrayContaining([
      expect.objectContaining({ kind: 'buff', name: '節制', num: 2 }),
      expect.objectContaining({ kind: 'debuff', name: '浪費', num: 1 }),
    ]))
  })

  it('preserves the server status order and renders live values through markers', () => {
    const actor = {
      ...heroActor(),
      statuses: { strength: 2, poison: 3, barrier: 1 },
      statusOrder: ['poison', 'barrier', 'strength'],
    }

    const buffs = toBuffs(actor)

    expect(buffs.map(buff => buff.name)).toEqual(['毒', 'バリア', '筋力'])
    expect(buffs[0].desc).toBe(
      'ターン開始時に[N:3]ダメージを受け、[S:poison]が1減少する。',
    )
    expect(buffs[1].desc).toContain('[S:barrier]が1減少する。')
  })

  it('maps every canonical buff and debuff to its own dedicated PNG icon', () => {
    const statusIcons: Record<string, string> = {
      strength: 'strength',
      dexterity: 'dexterity',
      omnistrike: 'omnistrike',
      barrier: 'barrier',
      lifesteal: 'lifesteal',
      reflect: 'reflect',
      handCostReduction: 'hand_cost_reduction',
      weak: 'weak',
      vulnerable: 'vulnerable',
      poison: 'poison',
      handCostIncrease: 'hand_cost_increase',
      vigor: 'vigor',
      pursuit: 'pursuit',
      regeneration: 'regeneration',
      fortify: 'fortify',
      resistance: 'resistance',
      revive: 'revive',
      growth: 'growth',
      insight: 'insight',
      haste: 'haste',
      continuity: 'continuity',
      mastery: 'mastery',
      virulentPoison: 'virulent_poison',
      stagnation: 'stagnation',
      burn: 'burn',
      scorch: 'scorch',
      confusion: 'confusion',
      curse: 'curse',
      bind: 'bind',
      silence: 'silence',
      seal: 'seal',
      exhaustion: 'exhaustion',
      distress: 'distress',
    }
    const actor = {
      ...heroActor(),
      statuses: Object.fromEntries(Object.keys(statusIcons).map(id => [id, 1])),
    }

    const buffs = toBuffs(actor)
    expect(buffs).toHaveLength(Object.keys(statusIcons).length)
    expect(buffs.map(buff => buff.icon)).toEqual(
      Object.values(statusIcons).map(file => `/assets/icons/status/${file}.png`),
    )
    expect(new Set(buffs.map(buff => buff.icon)).size).toBe(33)

    const [unknown] = toBuffs({ ...heroActor(), statuses: { futureStatus: 1 } })
    expect(unknown.icon).toBe('/assets/icons/status/status_unknown.png')
  })

  it('maps the new class statuses to their Japanese names and buff kinds', () => {
    const actor = {
      ...heroActor(),
      statuses: {
        afterimage: 2,
        steadfast: 1,
        cover: 3,
        defensiveStance: 2,
        comboCarryover: 1,
        afterglow: 1,
        resonance: 2,
        disruption: 2,
      },
    }

    const buffs = toBuffs(actor)
    expect(buffs.map(buff => [buff.name, buff.kind])).toEqual([
      ['残光', 'buff'],
      ['堅持', 'buff'],
      ['護衛', 'buff'],
      ['守勢', 'buff'],
      ['連環', 'buff'],
      ['余韻', 'buff'],
      ['共鳴', 'buff'],
      ['攪乱', 'debuff'],
    ])
    expect(buffs.map(buff => buff.icon)).toEqual([
      '/assets/icons/status/afterimage.png',
      '/assets/icons/status/steadfast.png',
      '/assets/icons/status/cover.png',
      '/assets/icons/status/defensive_stance.png',
      '/assets/icons/status/combo_carryover.png',
      '/assets/icons/status/afterglow.png',
      '/assets/icons/status/resonance.png',
      '/assets/icons/status/disruption.png',
    ])
    expect(buffs[0].desc).toContain('このターン最後の攻撃を[N:2]回実行')
    expect(buffs[7].desc).toContain('[S:disruption]が1減少')
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

    expect(demo.icon).toBe('/assets/icons/intent/attack_all.png')
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
    description: '', upgradedDescription: null, poolClass: 'Striker',
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
  it('preserves the authoritative armed Contingency marker', () => {
    const armed = battleCard('normal_5', 5)
    armed.isContingencyArmed = true
    armed.contingencyTriggerText = '相手のアタック時'

    expect(toHandCardDemo(armed, comboCatalog, 5)).toMatchObject({
      isContingencyArmed: true,
      contingencyTriggerText: '相手のアタック時',
    })

    expect(toHandCardDemo(battleCard('normal_5', 5), comboCatalog, 5)).toMatchObject({
      isContingencyArmed: false,
      contingencyTriggerText: null,
    })
  })

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
    expect(demo.poolClass).toBe('Striker')
  })

  it('prefers the battle DTO acquisition class over a stale catalog entry', () => {
    const card = { ...battleCard('normal_5', 5), poolClass: 'Guardian' as const }

    expect(toHandCardDemo(card, comboCatalog, 5).poolClass).toBe('Guardian')
  })

  it('uses the Universal frame when neither the DTO nor catalog identifies an affiliation', () => {
    const card = { ...battleCard('unknown_generated_card', 1), poolClass: null }

    expect(toHandCardDemo(card, null, 1).poolClass).toBe('Universal')
  })

  it('preserves the authoritative cost breakdown when changes cancel out', () => {
    const card = {
      ...battleCard('normal_5', 5, 5),
      costChanges: [
        { kind: 'modifier' as const, value: -1, sourceId: 'a', sourceName: '軽減元' },
        { kind: 'status' as const, value: 1, sourceId: 'b', sourceName: '浪費' },
      ],
    }

    expect(toHandCardDemo(card, comboCatalog, 5)).toMatchObject({
      cost: 5,
      costOrig: null,
      costOriginal: 5,
      costChanges: card.costChanges,
    })
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

  it('blocks a card whose per-turn effect limit is exhausted', () => {
    const card = {
      ...battleCard('normal_5', 5, 5),
      cardUsageLimitReached: true,
    }
    expect(toHandCardDemo(card, comboCatalog, 99)).toMatchObject({
      cardUsageLimitReached: true,
      costUnavailable: true,
      playable: false,
    })
  })

  it('blocks a card that Core marks as restricted by a status', () => {
    const card = {
      ...battleCard('normal_5', 5, 5),
      cardPlayRestricted: true,
      cardPlayRestrictionReason: 'bind',
    }
    expect(toHandCardDemo(card, comboCatalog, 99)).toMatchObject({
      costUnavailable: true,
      playable: false,
    })
  })

  it('keeps Continuity-only combo continuation separate from the ring highlight', () => {
    const card = {
      ...battleCard('normal_5', 5, 4),
      comboContinues: true,
      comboRingHighlighted: false,
    }

    expect(toHandCardDemo(card, comboCatalog, 99)).toMatchObject({
      comboContinues: true,
      comboRingHighlighted: false,
    })
  })
})
