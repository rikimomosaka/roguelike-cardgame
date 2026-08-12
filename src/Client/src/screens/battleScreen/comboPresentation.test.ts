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
