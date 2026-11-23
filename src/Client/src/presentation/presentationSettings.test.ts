import { describe, expect, it } from 'vitest'
import {
  DEFAULT_PRESENTATION_SETTINGS,
  getEffectiveMotionLevel,
  sanitizePresentationSettings,
} from './presentationSettings'

describe('presentation settings', () => {
  it('sanitizes partial and invalid persisted data field by field', () => {
    expect(sanitizePresentationSettings({
      motionLevel: 'cinematic',
      screenShake: false,
      flashes: 'yes',
      uiScale: 250,
    })).toEqual({
      ...DEFAULT_PRESENTATION_SETTINGS,
      screenShake: false,
    })
  })

  it('accepts every supported value', () => {
    expect(sanitizePresentationSettings({
      motionLevel: 'off',
      screenShake: false,
      flashes: false,
      uiScale: 110,
    })).toEqual({
      motionLevel: 'off',
      screenShake: false,
      flashes: false,
      uiScale: 110,
    })
  })

  it('gives the operating-system reduced-motion request priority', () => {
    expect(getEffectiveMotionLevel('full', true)).toBe('reduced')
    expect(getEffectiveMotionLevel('reduced', false)).toBe('reduced')
    expect(getEffectiveMotionLevel('off', true)).toBe('off')
  })
})
