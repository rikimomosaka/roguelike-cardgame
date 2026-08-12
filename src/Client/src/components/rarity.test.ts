import { describe, expect, it } from 'vitest'
import {
  optionalRarityCodeFromNumber,
  rarityCodeFromName,
  rarityCodeFromNumber,
} from './rarity'

describe('rarity catalog conversion', () => {
  it.each([
    [0, 'c'], // Promo uses the common presentation.
    [1, 'c'],
    [2, 'r'],
    [3, 'e'],
    [4, 'l'],
    [5, 't'],
  ] as const)('maps Core rarity %s to %s', (value, expected) => {
    expect(rarityCodeFromNumber(value)).toBe(expected)
  })

  it('preserves missing optional catalog values', () => {
    expect(optionalRarityCodeFromNumber(undefined)).toBeUndefined()
  })

  it.each([
    ['Common', 'c'],
    ['Rare', 'r'],
    ['Epic', 'e'],
    ['Legendary', 'l'],
    ['Token', 't'],
  ] as const)('maps named rarity %s to %s', (value, expected) => {
    expect(rarityCodeFromName(value)).toBe(expected)
  })
})
