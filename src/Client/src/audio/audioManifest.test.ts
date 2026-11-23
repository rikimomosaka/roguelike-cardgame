import { describe, expect, it } from 'vitest'
import { BGM_TRACKS, selectSe } from './audioManifest'

describe('audio manifest', () => {
  it('uses public-root URLs and distinct BGM ids', () => {
    const tracks = Object.values(BGM_TRACKS)
    expect(tracks.every(track => track.src.startsWith('/audio/bgm/'))).toBe(true)
    expect(new Set(tracks.map(track => track.id)).size).toBe(tracks.length)
  })

  it('selects a stable variant for the same semantic cue and seed', () => {
    expect(selectSe('cardDraw', 'card-42')).toEqual(selectSe('cardDraw', 'card-42'))
    expect(selectSe('hurt', 7).src).toMatch(/^\/audio\/se\/hurt-[123]\.ogg$/)
  })
})
