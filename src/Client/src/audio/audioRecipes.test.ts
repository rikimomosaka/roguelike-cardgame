import { describe, expect, it } from 'vitest'
import {
  panForBattleSlot,
  panFromStageX,
  selectBattleSoundRecipe,
  selectCharacterSkillSound,
} from './audioRecipes'
import { selectSe } from './audioManifest'

describe('deterministic sound selection', () => {
  it('applies repeatable pitch/gain variation and explicit stereo pan', () => {
    const first = selectSe('hurt', 'hit:42', { pan: 0.63 })
    const repeated = selectSe('hurt', 'hit:42', { pan: 0.63 })
    const different = selectSe('hurt', 'hit:43', { pan: 0.63 })

    expect(first).toEqual(repeated)
    expect(first.pan).toBe(0.63)
    expect(first.voiceGroup).toBe('hurt')
    expect(first.detuneCents).not.toBe(different.detuneCents)
  })

  it('can disable variation for exact reference playback', () => {
    const cue = selectSe('attack', 1, { vary: false, pan: -0.4 })
    expect(cue.pan).toBe(-0.4)
    expect(cue.detuneCents).toBeUndefined()
    expect(cue.playbackRate).toBeUndefined()
  })
})

describe('layered sound recipes', () => {
  it('builds swing/contact/block layers from the existing manifest', () => {
    const partial = selectBattleSoundRecipe('attackPartial', 'hit:5', { pan: 0.5 })
    expect(partial.layers).toHaveLength(3)
    expect(partial.layers.map(layer => layer.cue.voiceGroup))
      .toEqual(['attack', 'block', 'hurt'])
    expect(partial.layers.every(layer => layer.cue.pan === 0.5)).toBe(true)
    expect(partial.layers[1].delayMs).toBeGreaterThan(0)
  })

  it('keeps a common skill signature while changing the class accent', () => {
    const striker = selectCharacterSkillSound('ultimate', 'Striker', 8)
    const guardian = selectCharacterSkillSound('ultimate', 'Guardian', 8)
    expect(striker.layers[0].cue.voiceGroup).toBe('ultimate')
    expect(guardian.layers[0].cue.voiceGroup).toBe('ultimate')
    expect(striker.layers[1].cue.voiceGroup).toBe('attack')
    expect(guardian.layers[1].cue.voiceGroup).toBe('block')
  })
})

describe('battle stereo helpers', () => {
  it('keeps players left, enemies right, and stage positions restrained', () => {
    expect(panForBattleSlot('player', 0, 1)).toBeLessThan(0)
    expect(panForBattleSlot('enemy', 0, 1)).toBeGreaterThan(0)
    expect(panForBattleSlot('player', 0, 4))
      .toBeLessThan(panForBattleSlot('player', 3, 4))
    expect(panFromStageX(0)).toBe(-0.82)
    expect(panFromStageX(640)).toBe(0)
    expect(panFromStageX(1280)).toBe(0.82)
  })
})
