import { describe, expect, it } from 'vitest'
import { characterMotionCandidates, playedCardReleasesCalm } from './BattleScreen'

const base = { attack: null, isHit: false, isSkill: false, cardMotion: null, isDown: false, isStill: false } as const

describe('characterMotionCandidates', () => {
  it('idles normally', () => {
    expect(characterMotionCandidates(base)).toEqual(['idle'])
  })

  it('holds the still (eyes closed) frame in calm instead of idling, flinching or casting', () => {
    expect(characterMotionCandidates({ ...base, isStill: true, isHit: true, isSkill: true })).toEqual(['still'])
  })

  it('still attacks and goes down in calm', () => {
    expect(characterMotionCandidates({ ...base, isStill: true, attack: 'attackAlt' })).toEqual(['attackAlt', 'attack', 'still'])
    expect(characterMotionCandidates({ ...base, isStill: true, isDown: true })).toEqual(['down', 'still'])
  })

  it('plays the card motion that releases calm', () => {
    expect(characterMotionCandidates({ ...base, isStill: true, cardMotion: 'cast' })).toEqual(['cast', 'still'])
  })
})

const step = (kind: string, targetInstanceId: string | null, note: string | null = null) => ({
  event: { kind, targetInstanceId, note },
})

describe('playedCardReleasesCalm', () => {
  it('is true when the card resolution moves the caster out of calm', () => {
    const steps = [step('PlayCard', null), step('PhaseChanged', 'hero', 'calm->motion:effect')]
    expect(playedCardReleasesCalm(steps, 0, 'hero')).toBe(true)
  })

  it('ignores other actors and later card plays', () => {
    const steps = [
      step('PlayCard', null), step('PhaseChanged', 'enemy', 'calm->none:effect'),
      step('PlayCard', null), step('PhaseChanged', 'hero', 'calm->none:used'),
    ]
    expect(playedCardReleasesCalm(steps, 0, 'hero')).toBe(false)
  })
})
