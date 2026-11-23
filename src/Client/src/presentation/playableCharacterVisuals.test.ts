import { describe, expect, it } from 'vitest'
import {
  DEFAULT_CHARACTER_PORTRAIT,
  resolveCharacterPortrait,
} from './playableCharacterVisuals'

describe('resolveCharacterPortrait', () => {
  it('resolves a playable character to the shared bust-up directory', () => {
    expect(resolveCharacterPortrait('enhancer_lyra')).toBe(
      '/characters/portraits/enhancer_lyra.png',
    )
  })

  it.each([undefined, null, '', '  ', '../enemy', 'id/path'])(
    'falls back to the default portrait for %s',
    value => {
      expect(resolveCharacterPortrait(value)).toBe(DEFAULT_CHARACTER_PORTRAIT)
    },
  )
})
