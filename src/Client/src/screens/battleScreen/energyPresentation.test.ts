import { describe, expect, it } from 'vitest'
import {
  ENERGY_CARRYOVER_DESCRIPTION,
  formatEnergyOrbValue,
  formatEnergyTooltipStatusLine,
  formatEnergyTooltipTitle,
} from './energyPresentation'

describe('formatEnergyOrbValue', () => {
  it.each([
    { current: 0, expectedCurrent: '0' },
    { current: 1, expectedCurrent: '1' },
    { current: 9, expectedCurrent: '9' },
  ])('shows the turn-start refill amount beside one-digit current energy ($current)', ({ current, expectedCurrent }) => {
    expect(formatEnergyOrbValue(current, 3)).toEqual({
      currentText: expectedCurrent,
      recoveryText: '/3',
      capped: false,
    })
  })

  it.each([10, 42, 99])('shows only the current value for two digits (%i)', current => {
    expect(formatEnergyOrbValue(current, 3)).toEqual({
      currentText: `${current}`,
      recoveryText: null,
      capped: false,
    })
  })

  it.each([100, 101, 999])('caps three or more digits at 99+ (%i)', current => {
    expect(formatEnergyOrbValue(current, 3)).toEqual({
      currentText: '99+',
      recoveryText: null,
      capped: true,
    })
  })
})

describe('energy tooltip copy', () => {
  it('separates the name and small current/recovery status line', () => {
    expect(formatEnergyTooltipTitle()).toBe('エナジー')
    expect(formatEnergyTooltipStatusLine(1, 3))
      .toBe('(現在1 ターン開始時に3まで回復)')
  })

  it('explains the refill-to-target rule', () => {
    expect(ENERGY_CARRYOVER_DESCRIPTION).toBe(
      'カードをプレイするのに必要なリソース。ターン開始時、規定値に満たなければ規定値まで回復する。規定値より多く残っている時は、増えも減りもせずそのまま持ち越される。')
  })
})
