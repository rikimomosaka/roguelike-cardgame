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

  it('uses independent gauge thresholds of 3 and 6', () => {
    expect(getComboSkillAvailability({ ...ready, comboGauge: 2 })).toEqual({
      tacticalDisabled: true,
      ultimateDisabled: true,
    })
    expect(getComboSkillAvailability({ ...ready, comboGauge: 3 })).toEqual({
      tacticalDisabled: false,
      ultimateDisabled: true,
    })
    expect(getComboSkillAvailability({ ...ready, comboGauge: 5 })).toEqual({
      tacticalDisabled: false,
      ultimateDisabled: true,
    })
  })

  it('allows the ultimate with an empty hand when resources are available', () => {
    expect(getComboSkillAvailability({ ...ready, handCount: 0 })).toEqual({
      tacticalDisabled: false,
      ultimateDisabled: false,
    })
  })

  it('disables both skills while 封印 is active', () => {
    expect(getComboSkillAvailability({ ...ready, skillsRestricted: true })).toEqual({
      tacticalDisabled: true,
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
      .toBe('自身に筋力1を付与。\n自身に拡散1を付与。')
    expect(ULTIMATE_SKILL_DESCRIPTION)
      .toBe('3枚引く。\n自身に節制3を付与。')
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
