import { describe, expect, it } from 'vitest'
import {
  normalizeMotionPlaybackSpeed,
  scaledOneShotMotionDuration,
} from './motionPresentation'

describe('motionPresentation', () => {
  it('keeps every four-frame one-shot synchronized at replay 0.5x, 1x and 2x', () => {
    expect(scaledOneShotMotionDuration(480, 0.5)).toBe(960)
    expect(scaledOneShotMotionDuration(480, 1)).toBe(480)
    expect(scaledOneShotMotionDuration(480, 2)).toBe(240)
    expect(scaledOneShotMotionDuration(440, 0.5)).toBe(880)
    expect(scaledOneShotMotionDuration(440, 2)).toBe(220)
  })

  it('clamps malformed replay speeds to the supported readability floor', () => {
    expect(normalizeMotionPlaybackSpeed(undefined)).toBe(1)
    expect(normalizeMotionPlaybackSpeed(0)).toBe(0.25)
  })
})
