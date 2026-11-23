import { describe, expect, it } from 'vitest'
import type { BattleEventDto, BattleEventKind } from '../../api/types'
import {
  battleAudioCuesForEvent,
  battleContactSoundRecipe,
  battleSoundRecipeForEvent,
  bgmDuckPresetForEvent,
  comboToneTier,
  statusAudioCue,
} from './battleAudioCues'

function battleEvent(
  kind: BattleEventKind,
  overrides: Partial<BattleEventDto> = {},
): BattleEventDto {
  return {
    kind,
    order: 0,
    casterInstanceId: null,
    targetInstanceId: null,
    amount: null,
    cardId: null,
    note: null,
    blockedAmount: null,
    hitIndex: null,
    ...overrides,
  }
}

describe('battle audio cues', () => {
  it('distinguishes full damage, full block, and partial block', () => {
    expect(battleAudioCuesForEvent(battleEvent('DealDamage', {
      actualHpLoss: 7,
      blockedAmount: 0,
    }))).toEqual(['hurt'])
    expect(battleAudioCuesForEvent(battleEvent('DealDamage', {
      actualHpLoss: 0,
      blockedAmount: 7,
    }))).toEqual(['block'])
    expect(battleAudioCuesForEvent(battleEvent('DealDamage', {
      actualHpLoss: 3,
      blockedAmount: 4,
    }))).toEqual(['block', 'hurt'])
  })

  it('does not duplicate events already consumed by a grouped attack hit', () => {
    expect(battleAudioCuesForEvent(battleEvent('DealDamage', {
      actualHpLoss: 3,
      blockedAmount: 4,
    }), { groupedHit: true })).toEqual([])
    expect(battleAudioCuesForEvent(battleEvent('ApplyStatus', {
      note: 'poison',
      amount: 2,
    }), { groupedHit: true })).toEqual([])
    expect(battleAudioCuesForEvent(battleEvent('GainAttack', {
      note: 'single',
      amount: 6,
    }))).toEqual([])
  })

  it('distinguishes buffs, debuffs, tactical skills, ultimates and outcomes', () => {
    expect(statusAudioCue('strength', 2)).toBe('buff')
    expect(statusAudioCue('strength', -2)).toBe('debuff')
    expect(statusAudioCue('disruption', 1)).toBe('debuff')
    expect(battleAudioCuesForEvent(battleEvent('UseCharacterSkill', { note: 'tactical' })))
      .toEqual(['tactical'])
    expect(battleAudioCuesForEvent(battleEvent('UseCharacterSkill', { note: 'ultimate' })))
      .toEqual(['ultimate'])
    expect(battleAudioCuesForEvent(battleEvent('BattleEnd', { note: 'Victory' })))
      .toEqual(['victory'])
    expect(battleAudioCuesForEvent(battleEvent('BattleEnd', { note: 'Defeat' })))
      .toEqual([])
  })
})

describe('combo tone tier', () => {
  it('keeps the real combo count so milestone accents do not repeat after 15', () => {
    expect(Array.from({ length: 15 }, (_, index) => comboToneTier(index + 1)))
      .toEqual(Array.from({ length: 15 }, (_, index) => index + 1))
    expect(comboToneTier(16)).toBe(16)
    expect(comboToneTier(99)).toBe(99)
    expect(comboToneTier(0)).toBe(1)
  })

  it('does not invent a combo number for legacy events', () => {
    expect(comboToneTier(null)).toBeNull()
    expect(comboToneTier(undefined)).toBeNull()
  })
})

describe('rich battle sound plans', () => {
  it('selects layered contact recipes without duplicating grouped hits', () => {
    const partial = battleEvent('DealDamage', { actualHpLoss: 3, blockedAmount: 4 })
    expect(battleSoundRecipeForEvent(partial)).toBe('attackPartial')
    expect(battleSoundRecipeForEvent(partial, { groupedHit: true })).toBeNull()
    expect(battleSoundRecipeForEvent(battleEvent('DealDamage', {
      actualHpLoss: 0,
      blockedAmount: 8,
    }))).toBe('attackBlocked')
    expect(battleContactSoundRecipe(0, 9)).toBe('attackHit')
    expect(battleContactSoundRecipe(5, 0)).toBe('attackBlocked')
    expect(battleContactSoundRecipe(5, 3)).toBe('attackPartial')
    expect(battleContactSoundRecipe(0, 0)).toBeNull()
  })

  it('maps status, character skills, finishers and defeat to presentation plans', () => {
    expect(battleSoundRecipeForEvent(battleEvent('ApplyStatus', { note: 'poison', amount: 2 })))
      .toBe('poisonApplied')
    expect(battleSoundRecipeForEvent(battleEvent('ApplyStatus', { note: 'strength', amount: 2 })))
      .toBe('buffApplied')
    expect(bgmDuckPresetForEvent(battleEvent('UseCharacterSkill', { note: 'ultimate' })))
      .toBe('ultimate')
    expect(bgmDuckPresetForEvent(battleEvent('FinisherResolved'))).toBe('finisher')
    expect(bgmDuckPresetForEvent(battleEvent('BattleEnd', { note: 'Defeat' }))).toBe('defeat')
  })
})
