import { describe, expect, it, vi } from 'vitest'
import {
  BrowserAudioEngine,
  comboMilestone,
  comboToneFrequency,
  comboToneStep,
  type BgmMediaElement,
} from './audioEngine'

class FakeMedia implements BgmMediaElement {
  src = ''
  loop = false
  preload = ''
  currentTime = 0
  volume = 0
  readonly play = vi.fn(async (): Promise<void> => undefined)
  readonly pause = vi.fn()
  readonly removeAttribute = vi.fn(() => { this.src = '' })
  readonly load = vi.fn()
}

function createBgmEngine() {
  const media = [new FakeMedia(), new FakeMedia()]
  let index = 0
  const engine = new BrowserAudioEngine({
    createAudioElement: () => media[index++],
    createAudioContext: () => null,
  })
  return { engine, media }
}

describe('BrowserAudioEngine BGM', () => {
  it('queues BGM until unlock and applies master times BGM volume', async () => {
    const { engine, media } = createBgmEngine()
    engine.setSettings({ schemaVersion: 2, master: 50, bgm: 40, se: 80, voice: 80 })

    engine.playBgm({ id: 'menu', src: '/audio/menu.ogg' }, { fadeMs: 0 })
    expect(media[0].play).not.toHaveBeenCalled()

    await engine.unlock()

    expect(media[0].play).toHaveBeenCalledOnce()
    expect(media[0].src).toBe('/audio/menu.ogg')
    expect(media[0].volume).toBeCloseTo(0.2)
  })

  it('does not restart a BGM with the same id', async () => {
    const { engine, media } = createBgmEngine()
    engine.playBgm({ id: 'menu', src: '/audio/menu.ogg' }, { fadeMs: 0 })
    await engine.unlock()

    engine.playBgm({ id: 'menu', src: '/audio/different-file.ogg' }, { fadeMs: 0 })

    expect(media[0].play).toHaveBeenCalledOnce()
    expect(media[1].play).not.toHaveBeenCalled()
  })

  it('crossfades to the spare player and can duck BGM without restarting it', async () => {
    const { engine, media } = createBgmEngine()
    engine.playBgm({ id: 'menu', src: '/audio/menu.ogg' }, { fadeMs: 0 })
    await engine.unlock()

    engine.playBgm({ id: 'battle', src: '/audio/battle.ogg' }, { fadeMs: 0 })
    await Promise.resolve()

    expect(media[0].pause).toHaveBeenCalled()
    expect(media[1].play).toHaveBeenCalledOnce()
    engine.setBgmDuck(0.5, 0)
    expect(media[1].volume).toBeCloseTo(0.8 * 0.7 * 0.5)
    expect(media[1].play).toHaveBeenCalledOnce()
  })

  it('runs an automatic duck attack/hold/release envelope', async () => {
    vi.useFakeTimers()
    try {
      const { engine, media } = createBgmEngine()
      engine.playBgm({ id: 'battle', src: '/audio/battle.ogg' }, { fadeMs: 0 })
      await engine.unlock()

      engine.duckBgm({ multiplier: 0.4, attackMs: 0, holdMs: 100, releaseMs: 0 })
      expect(media[0].volume).toBeCloseTo(0.8 * 0.7 * 0.4)
      vi.advanceTimersByTime(100)
      expect(media[0].volume).toBeCloseTo(0.8 * 0.7)
    } finally {
      vi.useRealTimers()
    }
  })

  it('single-flights repeated unlocks while media playback is still pending', async () => {
    const { engine, media } = createBgmEngine()
    let resolvePlay!: () => void
    media[0].play.mockImplementationOnce(() => new Promise<void>((resolve) => {
      resolvePlay = resolve
    }))
    engine.playBgm({ id: 'menu', src: '/audio/menu.ogg' }, { fadeMs: 0 })

    const firstUnlock = engine.unlock()
    const secondUnlock = engine.unlock()

    expect(media[0].play).toHaveBeenCalledOnce()
    expect(media[1].play).not.toHaveBeenCalled()

    resolvePlay()
    await Promise.all([firstUnlock, secondUnlock])
    expect(media[0].play).toHaveBeenCalledOnce()
  })

  it('starts the newest queued track after an in-flight start settles', async () => {
    const { engine, media } = createBgmEngine()
    let resolveMenu!: () => void
    media[0].play.mockImplementationOnce(() => new Promise<void>((resolve) => {
      resolveMenu = resolve
    }))
    engine.playBgm({ id: 'menu', src: '/audio/menu.ogg' }, { fadeMs: 0 })
    const unlock = engine.unlock()

    engine.playBgm({ id: 'battle', src: '/audio/battle.ogg' }, { fadeMs: 0 })
    expect(media[1].play).not.toHaveBeenCalled()

    resolveMenu()
    await unlock
    await vi.waitFor(() => expect(media[0].play).toHaveBeenCalledTimes(2))
    expect(media[0].src).toBe('/audio/battle.ogg')
  })
})

describe('BrowserAudioEngine buses', () => {
  it('keeps master, SE, and future voice gains independent', async () => {
    const gains: Array<{ gain: { value: number }; connect: ReturnType<typeof vi.fn> }> = []
    const context = {
      state: 'running',
      destination: {},
      createGain: () => {
        const gain = { gain: { value: 1 }, connect: vi.fn() }
        gains.push(gain)
        return gain
      },
      resume: vi.fn(async () => undefined),
      close: vi.fn(async () => undefined),
    } as unknown as AudioContext
    const engine = new BrowserAudioEngine({
      createAudioElement: () => null,
      createAudioContext: () => context,
    })

    await engine.unlock()
    engine.setSettings({ schemaVersion: 2, master: 50, bgm: 40, se: 25, voice: 90 })

    expect(gains[0].gain.value).toBe(0.5)
    expect(gains[1].gain.value).toBe(0.25)
    expect(gains[2].gain.value).toBe(0.9)
  })
})

describe('combo tone scale', () => {
  it('has fifteen distinct ascending steps and clamps later combos to step 15', () => {
    const frequencies = Array.from({ length: 15 }, (_, index) => comboToneFrequency(index + 1))
    expect(new Set(frequencies).size).toBe(15)
    expect(frequencies.every((frequency, index) => index === 0 || frequency > frequencies[index - 1]))
      .toBe(true)
    expect(comboToneFrequency(16)).toBe(comboToneFrequency(15))
    expect(comboToneFrequency(999)).toBe(comboToneFrequency(15))
  })

  it('uses the first note for a reset, invalid, or non-positive combo', () => {
    expect(comboToneStep(0)).toBe(1)
    expect(comboToneStep(-10)).toBe(1)
    expect(comboToneStep(Number.NaN)).toBe(1)
  })

  it('marks only the 5, 10, and 15 combo accents', () => {
    expect(comboMilestone(4)).toBeNull()
    expect(comboMilestone(5)).toBe(5)
    expect(comboMilestone(10)).toBe(10)
    expect(comboMilestone(15)).toBe(15)
    expect(comboMilestone(16)).toBeNull()
  })

  it('layers milestone harmonics while later combos retain one clamped lead tone', async () => {
    const oscillators: Array<{ frequency: number }> = []
    const context = {
      state: 'running',
      currentTime: 0,
      destination: {},
      createGain: () => ({
        gain: {
          value: 1,
          setValueAtTime: vi.fn(),
          exponentialRampToValueAtTime: vi.fn(),
        },
        connect: vi.fn(),
      }),
      createOscillator: () => {
        const state = { frequency: 0 }
        oscillators.push(state)
        return {
          type: 'sine',
          frequency: {
            setValueAtTime: (frequency: number) => { state.frequency = frequency },
          },
          connect: vi.fn(),
          start: vi.fn(),
          stop: vi.fn(),
        }
      },
      resume: vi.fn(async () => undefined),
      close: vi.fn(async () => undefined),
    } as unknown as AudioContext
    const engine = new BrowserAudioEngine({
      createAudioElement: () => null,
      createAudioContext: () => context,
    })
    await engine.unlock()

    expect(engine.playComboTone(4)).toBe(true)
    expect(oscillators).toHaveLength(1)
    expect(engine.playComboTone(5)).toBe(true)
    expect(oscillators).toHaveLength(3)
    expect(engine.playComboTone(10)).toBe(true)
    expect(oscillators).toHaveLength(6)
    expect(engine.playComboTone(16)).toBe(true)
    expect(oscillators).toHaveLength(7)
    expect(oscillators[6].frequency).toBeCloseTo(comboToneFrequency(15))
  })
})

describe('BrowserAudioEngine rich SE playback', () => {
  it('uses a stereo panner, applies detune, and limits a shared voice group', async () => {
    const panners: Array<{
      pan: { value: number }
      connect: ReturnType<typeof vi.fn>
      disconnect: ReturnType<typeof vi.fn>
    }> = []
    const sources: Array<{
      buffer: AudioBuffer | null
      playbackRate: { value: number }
      detune: { setValueAtTime: ReturnType<typeof vi.fn> }
      connect: ReturnType<typeof vi.fn>
      disconnect: ReturnType<typeof vi.fn>
      start: ReturnType<typeof vi.fn>
      onended: (() => void) | null
    }> = []
    const context = {
      state: 'running',
      currentTime: 0,
      destination: {},
      createGain: () => ({
        gain: {
          value: 1,
          setValueAtTime: vi.fn(),
          exponentialRampToValueAtTime: vi.fn(),
        },
        connect: vi.fn(),
        disconnect: vi.fn(),
      }),
      createStereoPanner: () => {
        const panner = { pan: { value: 0 }, connect: vi.fn(), disconnect: vi.fn() }
        panners.push(panner)
        return panner
      },
      createBufferSource: () => {
        const source = {
          buffer: null,
          playbackRate: { value: 1 },
          detune: { setValueAtTime: vi.fn() },
          connect: vi.fn(),
          disconnect: vi.fn(),
          start: vi.fn(),
          onended: null,
        }
        sources.push(source)
        return source
      },
      decodeAudioData: vi.fn(async () => ({} as AudioBuffer)),
      resume: vi.fn(async () => undefined),
      close: vi.fn(async () => undefined),
    } as unknown as AudioContext
    const engine = new BrowserAudioEngine({
      createAudioElement: () => null,
      createAudioContext: () => context,
      fetchAudio: vi.fn(async () => ({
        ok: true,
        arrayBuffer: async () => new ArrayBuffer(2),
      } as Response)),
    })
    await engine.unlock()

    expect(await engine.playSe({
      id: 'hurt-a',
      src: '/a.ogg',
      pan: 0.62,
      detuneCents: 24,
      voiceGroup: 'impact',
      maxGroupVoices: 1,
    })).toBe(true)
    expect(panners[0].pan.value).toBe(0.62)
    expect(sources[0].detune.setValueAtTime).toHaveBeenCalledWith(24, 0)

    expect(await engine.playSe({
      id: 'hurt-b',
      src: '/b.ogg',
      voiceGroup: 'impact',
      maxGroupVoices: 1,
    })).toBe(false)

    sources[0].onended?.()
    expect(await engine.playSe({
      id: 'hurt-b',
      src: '/b.ogg',
      voiceGroup: 'impact',
      maxGroupVoices: 1,
    })).toBe(true)
  })
})
