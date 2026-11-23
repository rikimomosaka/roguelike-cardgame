import { describe, expect, it } from 'vitest'
import { formatComboDescription, formatMaxComboThisTurn } from './comboPresentation'

describe('formatComboDescription', () => {
  it('describes the exact next-cost condition during a normal combo', () => {
    expect(formatComboDescription({
      count: 4,
      lastPlayedOrigCost: 2,
      nextCardComboFreePass: false,
    })).toBe('現在4コンボ。コスト3のカードをプレイする時、1コスト軽減してコンボ継続。')
  })

  it('describes the arbitrary-card pass without excluding Wild-family cards', () => {
    expect(formatComboDescription({
      count: 2,
      lastPlayedOrigCost: 7,
      nextCardComboFreePass: true,
    })).toBe('現在2コンボ。任意のカードをプレイする時、1コスト軽減してコンボ継続。')
  })

  it('describes 連環 carryover as an arbitrary full-cost continuation', () => {
    expect(formatComboDescription({
      count: 3,
      lastPlayedOrigCost: null,
      nextCardComboFreePass: false,
      comboCarryoverPending: true,
    })).toBe('現在3コンボ。連環により、次の1枚は任意コストでコンボ継続、軽減なし。')

    expect(formatComboDescription({
      count: 0,
      lastPlayedOrigCost: null,
      nextCardComboFreePass: false,
      comboCarryoverPending: true,
    })).toBe('現在0コンボ。連環により、次の1枚は任意コストでコンボ継続、軽減なし。')
  })

  it('uses a start-combo explanation before the first card', () => {
    expect(formatComboDescription({
      count: 0,
      lastPlayedOrigCost: null,
      nextCardComboFreePass: false,
    })).toBe('現在0コンボ。カードをプレイするとコンボ開始。')
  })
})

describe('formatMaxComboThisTurn', () => {
  it('formats the independent turn-maximum line exactly', () => {
    expect(formatMaxComboThisTurn(4)).toBe('(このターンの最大コンボ:4)')
  })
})
