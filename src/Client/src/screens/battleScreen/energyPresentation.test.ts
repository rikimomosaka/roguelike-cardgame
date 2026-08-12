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
  ])('shows recovery beside one-digit current energy ($current)', ({ current, expectedCurrent }) => {
    expect(formatEnergyOrbValue(current, 3)).toEqual({
      currentText: expectedCurrent,
      recoveryText: '/+3',
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
    expect(formatEnergyTooltipStatusLine(3, 3))
      .toBe('(現在3 ターン開始時に3追加)')
  })

  it('explains that unused energy carries over', () => {
    expect(ENERGY_CARRYOVER_DESCRIPTION).toContain('次のターンへ持ち越され')
    expect(ENERGY_CARRYOVER_DESCRIPTION).toContain('ターン開始時に追加分を得る')
    expect(ENERGY_CARRYOVER_DESCRIPTION).not.toContain('最大値まで回復')
  })
})
