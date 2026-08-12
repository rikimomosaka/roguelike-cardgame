import { describe, expect, it } from 'vitest'
import {
  clampComboGauge,
  getComboSkillAvailability,
  TACTICAL_SKILL_DESCRIPTION,
  ULTIMATE_SKILL_DESCRIPTION,
} from './comboSkillPresentation'

const ready = {
  comboGauge: 10,
  phase: 'PlayerInput' as const,
  animating: false,
  busy: false,
  hasPendingChoice: false,
  handCount: 1,
  tacticalUsedThisTurn: false,
}

describe('getComboSkillAvailability', () => {
  it('enables both skills when every condition is satisfied', () => {
    expect(getComboSkillAvailability(ready)).toEqual({
      tacticalDisabled: false,
      ultimateDisabled: false,
    })
  })

  it.each([
    { name: 'animation', values: { animating: true } },
    { name: 'request', values: { busy: true } },
    { name: 'non-input phase', values: { phase: 'PlayerAttacking' as const } },
    { name: 'pending choice', values: { hasPendingChoice: true } },
  ])('disables both during $name', ({ values }) => {
    expect(getComboSkillAvailability({ ...ready, ...values })).toEqual({
      tacticalDisabled: true,
      ultimateDisabled: true,
    })
  })

  it('uses independent gauge thresholds of 3 and 10', () => {
    expect(getComboSkillAvailability({ ...ready, comboGauge: 2 })).toEqual({
      tacticalDisabled: true,
      ultimateDisabled: true,
    })
    expect(getComboSkillAvailability({ ...ready, comboGauge: 3 })).toEqual({
      tacticalDisabled: false,
      ultimateDisabled: true,
    })
    expect(getComboSkillAvailability({ ...ready, comboGauge: 9 })).toEqual({
      tacticalDisabled: false,
      ultimateDisabled: true,
    })
  })

  it('disables only the ultimate when the hand is empty', () => {
    expect(getComboSkillAvailability({ ...ready, handCount: 0 })).toEqual({
      tacticalDisabled: false,
      ultimateDisabled: true,
    })
  })

  it('disables only the once-per-turn tactical after it has been used', () => {
    expect(getComboSkillAvailability({ ...ready, tacticalUsedThisTurn: true })).toEqual({
      tacticalDisabled: true,
      ultimateDisabled: false,
    })
  })
})

describe('combo skill presentation constants', () => {
  it('keeps the exact skill explanations', () => {
    expect(TACTICAL_SKILL_DESCRIPTION)
      .toBe('1ターンに1度、自身の攻撃+1と拡散+1を得る。拡散がある間、攻撃はすべて全体攻撃になる。')
    expect(ULTIMATE_SKILL_DESCRIPTION)
      .toBe('発動時の手札それぞれに、その時点のコストと同量の軽減をこのターン終了まで与える。カードを使うと軽減は失われる。')
  })

  it.each([
    [-1, 0],
    [0, 0],
    [7, 7],
    [10, 10],
    [11, 10],
  ])('clamps gauge %i to %i', (value, expected) => {
    expect(clampComboGauge(value)).toBe(expected)
  })
})
