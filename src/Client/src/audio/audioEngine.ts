import type { AudioSettingsDto } from '../api/types'
import { DEFAULT_AUDIO_SETTINGS } from '../hooks/useAudioSettings'

export type AudioBus = 'se' | 'voice'

export type BgmTrack = {
  id: string
  src: string
  loop?: boolean
  volume?: number
}

export type BgmTransitionOptions = {
  fadeMs?: number
}

export type AudioCue = {
  id: string
  src: string
  volume?: number
  playbackRate?: number
  /** Stereo position: -1 is left, 0 is centre, and 1 is right. */
  pan?: number
  /** Fine pitch adjustment. Falls back to playbackRate on older browsers. */
  detuneCents?: number
  cooldownMs?: number
  maxVoices?: number
  /** Related cues share this limiter even when their asset ids differ. */
  voiceGroup?: string
  maxGroupVoices?: number
}

export type AudioLayer = {
  cue: AudioCue
  delayMs?: number
}

export type LayeredAudioCue = {
  id: string
  layers: readonly AudioLayer[]
  cooldownMs?: number
}

export type BgmDuckEnvelopeOptions = {
  multiplier?: number
  attackMs?: number
  holdMs?: number
  releaseMs?: number
}

export type BgmMediaElement = {
  src: string
  loop: boolean
  preload: string
  currentTime: number
  volume: number
  play: () => Promise<void> | void
  pause: () => void
  removeAttribute?: (name: string) => void
  load?: () => void
}

export interface AudioEngine {
  setSettings(settings: AudioSettingsDto): void
  playBgm(track: BgmTrack, options?: BgmTransitionOptions): void
  stopBgm(options?: BgmTransitionOptions): void
  setBgmDuck(multiplier: number, rampMs?: number): void
  duckBgm(options?: BgmDuckEnvelopeOptions): void
  playSe(cue: AudioCue): Promise<boolean>
  playLayeredSe(cue: LayeredAudioCue): Promise<number>
  playVoice(cue: AudioCue): Promise<boolean>
  playComboTone(combo: number): boolean
  unlock(): Promise<void>
  destroy(): void
}

export type BrowserAudioEngineOptions = {
  createAudioElement?: () => BgmMediaElement | null
  createAudioContext?: () => AudioContext | null
  fetchAudio?: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>
  now?: () => number
}

type BgmSlot = {
  media: BgmMediaElement | null
  track: BgmTrack | null
  fadeGain: number
}

type PendingBgm = {
  track: BgmTrack
  fadeMs: number
}

const DEFAULT_BGM_FADE_MS = 800
const DEFAULT_DUCK_RAMP_MS = 120
const DEFAULT_DUCK_MULTIPLIER = 0.48
const DEFAULT_DUCK_HOLD_MS = 170
const DEFAULT_DUCK_RELEASE_MS = 360
const MAX_GLOBAL_VOICES = 24
const COMBO_SCALE_SEMITONES = Object.freeze([
  0, 2, 4, 5, 7, 9, 11,
  12, 14, 16, 17, 19, 21, 23, 24,
])

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value))
}

function normalizedVolume(value: number): number {
  return clamp(value, 0, 100) / 100
}

function defaultAudioElementFactory(): BgmMediaElement | null {
  if (typeof Audio === 'undefined') return null
  const media = new Audio()
  media.preload = 'auto'
  return media
}

function defaultAudioContextFactory(): AudioContext | null {
  if (typeof window === 'undefined') return null
  const Context = window.AudioContext
    ?? (window as typeof window & { webkitAudioContext?: typeof AudioContext }).webkitAudioContext
  return Context ? new Context() : null
}

export function comboToneStep(combo: number): number {
  const normalized = Number.isFinite(combo) ? Math.floor(combo) : 1
  return clamp(normalized, 1, COMBO_SCALE_SEMITONES.length)
}

export function comboToneFrequency(combo: number, baseFrequency = 261.625565): number {
  const semitones = COMBO_SCALE_SEMITONES[comboToneStep(combo) - 1]
  return baseFrequency * (2 ** (semitones / 12))
}

export function comboMilestone(combo: number): 5 | 10 | 15 | null {
  const normalized = Number.isFinite(combo) ? Math.floor(combo) : 0
  if (normalized === 5 || normalized === 10 || normalized === 15) return normalized
  return null
}

export class BrowserAudioEngine implements AudioEngine {
  private settings: AudioSettingsDto = DEFAULT_AUDIO_SETTINGS
  private readonly slots: [BgmSlot, BgmSlot]
  private activeSlot = -1
  private pendingBgm: PendingBgm | null = null
  private bgmGeneration = 0
  private bgmStartInFlight: Promise<void> | null = null
  private unlocked = false
  private destroyed = false
  private bgmDuck = 1
  private bgmFadeTimer: ReturnType<typeof setInterval> | null = null
  private duckTimer: ReturnType<typeof setInterval> | null = null
  private duckReleaseTimer: ReturnType<typeof setTimeout> | null = null

  private context: AudioContext | null = null
  private masterGain: GainNode | null = null
  private seGain: GainNode | null = null
  private voiceGain: GainNode | null = null
  private readonly bufferCache = new Map<string, Promise<AudioBuffer>>()
  private readonly activeVoices = new Map<string, number>()
  private readonly activeVoiceGroups = new Map<string, number>()
  private readonly lastCueAt = new Map<string, number>()
  private readonly lastLayeredCueAt = new Map<string, number>()
  private activeVoiceTotal = 0

  private readonly createAudioContext: () => AudioContext | null
  private readonly fetchAudio: (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>
  private readonly now: () => number

  constructor(options: BrowserAudioEngineOptions = {}) {
    const createAudioElement = options.createAudioElement ?? defaultAudioElementFactory
    this.createAudioContext = options.createAudioContext ?? defaultAudioContextFactory
    this.fetchAudio = options.fetchAudio ?? ((input, init) => fetch(input, init))
    this.now = options.now ?? (() => (
      typeof performance === 'undefined' ? Date.now() : performance.now()
    ))
    this.slots = [
      { media: createAudioElement(), track: null, fadeGain: 0 },
      { media: createAudioElement(), track: null, fadeGain: 0 },
    ]
  }

  setSettings(settings: AudioSettingsDto): void {
    this.settings = {
      schemaVersion: 2,
      master: clamp(settings.master, 0, 100),
      bgm: clamp(settings.bgm, 0, 100),
      se: clamp(settings.se, 0, 100),
      voice: clamp(settings.voice, 0, 100),
    }
    this.applyBgmVolumes()
    this.applyBusVolumes()
  }

  playBgm(track: BgmTrack, options: BgmTransitionOptions = {}): void {
    if (this.destroyed) return
    const activeTrack = this.activeSlot >= 0 ? this.slots[this.activeSlot].track : null
    if (activeTrack?.id === track.id) {
      this.pendingBgm = null
      return
    }
    if (this.pendingBgm?.track.id === track.id) return

    this.pendingBgm = {
      track,
      fadeMs: Math.max(0, options.fadeMs ?? DEFAULT_BGM_FADE_MS),
    }
    this.bgmGeneration += 1
    if (this.unlocked) void this.queuePendingBgmStart()
  }

  stopBgm(options: BgmTransitionOptions = {}): void {
    this.pendingBgm = null
    this.bgmGeneration += 1
    if (this.activeSlot < 0 && this.slots.every((slot) => !slot.track)) return
    const startGains = this.slots.map((slot) => slot.fadeGain)
    const duration = Math.max(0, options.fadeMs ?? DEFAULT_BGM_FADE_MS)
    this.animateBgm(duration, (progress) => {
      this.slots.forEach((slot, index) => {
        slot.fadeGain = startGains[index] * (1 - progress)
      })
      this.applyBgmVolumes()
    }, () => {
      for (const slot of this.slots) this.stopSlot(slot)
      this.activeSlot = -1
    })
  }

  setBgmDuck(multiplier: number, rampMs = DEFAULT_DUCK_RAMP_MS): void {
    if (this.duckReleaseTimer !== null) clearTimeout(this.duckReleaseTimer)
    this.duckReleaseTimer = null
    this.rampBgmDuck(multiplier, rampMs)
  }

  duckBgm(options: BgmDuckEnvelopeOptions = {}): void {
    if (this.destroyed) return
    if (this.duckReleaseTimer !== null) clearTimeout(this.duckReleaseTimer)
    this.duckReleaseTimer = null

    const attackMs = Math.max(0, options.attackMs ?? 55)
    const holdMs = Math.max(0, options.holdMs ?? DEFAULT_DUCK_HOLD_MS)
    this.rampBgmDuck(options.multiplier ?? DEFAULT_DUCK_MULTIPLIER, attackMs)
    this.duckReleaseTimer = setTimeout(() => {
      this.duckReleaseTimer = null
      this.rampBgmDuck(1, options.releaseMs ?? DEFAULT_DUCK_RELEASE_MS)
    }, attackMs + holdMs)
  }

  private rampBgmDuck(multiplier: number, rampMs: number): void {
    const target = clamp(multiplier, 0, 1)
    if (this.duckTimer !== null) clearInterval(this.duckTimer)
    this.duckTimer = null

    const duration = Math.max(0, rampMs)
    if (duration === 0 || this.destroyed) {
      this.bgmDuck = target
      this.applyBgmVolumes()
      return
    }

    const startedAt = this.now()
    const initial = this.bgmDuck
    this.duckTimer = setInterval(() => {
      const progress = clamp((this.now() - startedAt) / duration, 0, 1)
      this.bgmDuck = initial + ((target - initial) * progress)
      this.applyBgmVolumes()
      if (progress >= 1 && this.duckTimer !== null) {
        clearInterval(this.duckTimer)
        this.duckTimer = null
      }
    }, 16)
  }

  playSe(cue: AudioCue): Promise<boolean> {
    return this.playBuffer(cue, 'se')
  }

  async playLayeredSe(layeredCue: LayeredAudioCue): Promise<number> {
    if (!this.unlocked || this.destroyed) return 0
    const now = this.now()
    const lastStartedAt = this.lastLayeredCueAt.get(layeredCue.id) ?? Number.NEGATIVE_INFINITY
    if (now - lastStartedAt < Math.max(0, layeredCue.cooldownMs ?? 0)) return 0
    this.lastLayeredCueAt.set(layeredCue.id, now)

    const results = await Promise.all(layeredCue.layers.map(async ({ cue, delayMs = 0 }) => {
      if (delayMs > 0) {
        await new Promise<void>((resolve) => setTimeout(resolve, delayMs))
      }
      return this.playBuffer(cue, 'se')
    }))
    return results.filter(Boolean).length
  }

  playVoice(cue: AudioCue): Promise<boolean> {
    return this.playBuffer(cue, 'voice')
  }

  playComboTone(combo: number): boolean {
    if (!this.unlocked || this.destroyed) return false
    const context = this.ensureContext()
    if (!context || !this.seGain) return false

    const startedAt = context.currentTime
    this.playSynthNote(context, this.seGain, {
      frequency: comboToneFrequency(combo),
      gain: 0.24,
      startedAt,
      duration: 0.2,
      type: 'triangle',
    })

    // Milestones add a quiet fifth/octave shimmer while preserving the exact
    // ascending 1..15 lead tone requested for gameplay readability.
    const milestone = comboMilestone(combo)
    if (milestone !== null) {
      const base = comboToneFrequency(combo)
      this.playSynthNote(context, this.seGain, {
        frequency: base * (2 ** (7 / 12)),
        gain: milestone === 5 ? 0.065 : 0.082,
        startedAt: startedAt + 0.018,
        duration: 0.25,
        type: 'sine',
      })
      if (milestone >= 10) {
        this.playSynthNote(context, this.seGain, {
          frequency: base * 2,
          gain: milestone === 10 ? 0.052 : 0.07,
          startedAt: startedAt + 0.032,
          duration: 0.28,
          type: 'sine',
        })
      }
    }
    return true
  }

  async unlock(): Promise<void> {
    if (this.destroyed) return
    this.unlocked = true
    const context = this.ensureContext()
    // Invoke both playback APIs before the first await. Some browsers consume
    // transient user activation after an async boundary, even when unlock was
    // entered directly from pointerdown/keydown.
    const resume = context?.state === 'suspended'
      ? context.resume().catch(() => undefined)
      : Promise.resolve()
    const bgmStart = this.pendingBgm
      ? this.queuePendingBgmStart()
      : Promise.resolve()
    await Promise.all([resume, bgmStart])
  }

  destroy(): void {
    this.destroyed = true
    this.pendingBgm = null
    this.bgmGeneration += 1
    if (this.bgmFadeTimer !== null) clearInterval(this.bgmFadeTimer)
    if (this.duckTimer !== null) clearInterval(this.duckTimer)
    if (this.duckReleaseTimer !== null) clearTimeout(this.duckReleaseTimer)
    this.bgmFadeTimer = null
    this.duckTimer = null
    this.duckReleaseTimer = null
    for (const slot of this.slots) this.stopSlot(slot)
    if (this.context) void this.context.close().catch(() => undefined)
    this.context = null
  }

  private queuePendingBgmStart(): Promise<void> {
    if (this.bgmStartInFlight) return this.bgmStartInFlight

    const attemptedGeneration = this.bgmGeneration
    const task = this.startPendingBgm(attemptedGeneration).finally(() => {
      if (this.bgmStartInFlight !== task) return
      this.bgmStartInFlight = null

      // A route may request a different track while the previous media.play()
      // is still settling. Start that newest request only after the old attempt
      // has finished so two slots never race to become active. A failure for the
      // same request stays pending and is retried by the next user gesture.
      if (!this.destroyed
        && this.unlocked
        && this.pendingBgm
        && this.bgmGeneration !== attemptedGeneration) {
        void this.queuePendingBgmStart()
      }
    })
    this.bgmStartInFlight = task
    return task
  }

  private async startPendingBgm(generation: number): Promise<void> {
    const pending = this.pendingBgm
    if (!pending || !this.unlocked || this.destroyed) return

    const nextIndex = this.activeSlot === 0 ? 1 : 0
    const next = this.slots[nextIndex]
    this.stopSlot(next)
    if (!next.media) return

    next.track = pending.track
    next.fadeGain = 0
    next.media.src = pending.track.src
    next.media.loop = pending.track.loop ?? true
    next.media.preload = 'auto'
    next.media.currentTime = 0
    this.applyBgmVolumes()

    try {
      await Promise.resolve(next.media.play())
    } catch {
      // Browser autoplay policy or a transient load failure: retain the request.
      return
    }

    if (generation !== this.bgmGeneration || this.pendingBgm?.track.id !== pending.track.id) {
      if (next.track?.id === pending.track.id) this.stopSlot(next)
      return
    }

    const previousIndex = this.activeSlot
    const previous = previousIndex >= 0 ? this.slots[previousIndex] : null
    const previousStart = previous?.fadeGain ?? 0
    this.activeSlot = nextIndex
    this.pendingBgm = null

    this.animateBgm(pending.fadeMs, (progress) => {
      // Equal-power crossfade keeps the perceived loudness stable around midpoint.
      next.fadeGain = Math.sin(progress * Math.PI * 0.5)
      if (previous) previous.fadeGain = previousStart * Math.cos(progress * Math.PI * 0.5)
      this.applyBgmVolumes()
    }, () => {
      next.fadeGain = 1
      if (previous) this.stopSlot(previous)
      this.applyBgmVolumes()
    })
  }

  private animateBgm(
    duration: number,
    update: (progress: number) => void,
    completed: () => void,
  ): void {
    if (this.bgmFadeTimer !== null) clearInterval(this.bgmFadeTimer)
    this.bgmFadeTimer = null
    if (duration === 0) {
      update(1)
      completed()
      return
    }

    const startedAt = this.now()
    update(0)
    this.bgmFadeTimer = setInterval(() => {
      const progress = clamp((this.now() - startedAt) / duration, 0, 1)
      update(progress)
      if (progress >= 1 && this.bgmFadeTimer !== null) {
        clearInterval(this.bgmFadeTimer)
        this.bgmFadeTimer = null
        completed()
      }
    }, 16)
  }

  private applyBgmVolumes(): void {
    const bus = normalizedVolume(this.settings.master)
      * normalizedVolume(this.settings.bgm)
      * this.bgmDuck
    for (const slot of this.slots) {
      if (!slot.media) continue
      const trackVolume = clamp(slot.track?.volume ?? 1, 0, 1)
      slot.media.volume = clamp(bus * trackVolume * slot.fadeGain, 0, 1)
    }
  }

  private stopSlot(slot: BgmSlot): void {
    if (slot.media) {
      slot.media.pause()
      slot.media.volume = 0
      try {
        slot.media.currentTime = 0
      } catch {
        // A not-yet-loaded media element may reject seeking; it is paused anyway.
      }
      if (slot.media.removeAttribute) {
        slot.media.removeAttribute('src')
        slot.media.load?.()
      } else {
        slot.media.src = ''
      }
    }
    slot.track = null
    slot.fadeGain = 0
  }

  private ensureContext(): AudioContext | null {
    if (this.context || this.destroyed) return this.context
    this.context = this.createAudioContext()
    if (!this.context) return null

    this.masterGain = this.context.createGain()
    this.seGain = this.context.createGain()
    this.voiceGain = this.context.createGain()
    this.seGain.connect(this.masterGain)
    this.voiceGain.connect(this.masterGain)
    this.masterGain.connect(this.context.destination)
    this.applyBusVolumes()
    return this.context
  }

  private applyBusVolumes(): void {
    if (!this.masterGain || !this.seGain || !this.voiceGain) return
    this.masterGain.gain.value = normalizedVolume(this.settings.master)
    this.seGain.gain.value = normalizedVolume(this.settings.se)
    this.voiceGain.gain.value = normalizedVolume(this.settings.voice)
  }

  private async playBuffer(cue: AudioCue, bus: AudioBus): Promise<boolean> {
    if (!this.unlocked || this.destroyed) return false
    const context = this.ensureContext()
    const destination = bus === 'se' ? this.seGain : this.voiceGain
    if (!context || !destination) return false

    const now = this.now()
    const lastStartedAt = this.lastCueAt.get(cue.id) ?? Number.NEGATIVE_INFINITY
    if (now - lastStartedAt < Math.max(0, cue.cooldownMs ?? 0)) return false
    if ((this.activeVoices.get(cue.id) ?? 0) >= Math.max(1, cue.maxVoices ?? 8)) return false
    const voiceGroup = cue.voiceGroup ?? cue.id
    if ((this.activeVoiceGroups.get(voiceGroup) ?? 0)
      >= Math.max(1, cue.maxGroupVoices ?? cue.maxVoices ?? 8)) return false
    if (this.activeVoiceTotal >= MAX_GLOBAL_VOICES) return false
    this.lastCueAt.set(cue.id, now)
    this.activeVoices.set(cue.id, (this.activeVoices.get(cue.id) ?? 0) + 1)
    this.activeVoiceGroups.set(voiceGroup, (this.activeVoiceGroups.get(voiceGroup) ?? 0) + 1)
    this.activeVoiceTotal += 1

    try {
      const buffer = await this.loadBuffer(cue.src, context)
      if (this.destroyed) {
        this.releaseVoice(cue.id, voiceGroup)
        return false
      }
      const source = context.createBufferSource()
      const cueGain = context.createGain()
      const createStereoPanner = context.createStereoPanner?.bind(context)
      const panner = createStereoPanner ? createStereoPanner() : null
      source.buffer = buffer
      const playbackRate = clamp(cue.playbackRate ?? 1, 0.25, 4)
      const detuneCents = clamp(cue.detuneCents ?? 0, -1200, 1200)
      source.playbackRate.value = source.detune
        ? playbackRate
        : clamp(playbackRate * (2 ** (detuneCents / 1200)), 0.25, 4)
      source.detune?.setValueAtTime(detuneCents, context.currentTime)
      cueGain.gain.value = clamp(cue.volume ?? 1, 0, 1)
      source.connect(cueGain)
      if (panner) {
        panner.pan.value = clamp(cue.pan ?? 0, -1, 1)
        cueGain.connect(panner)
        panner.connect(destination)
      } else {
        cueGain.connect(destination)
      }

      source.onended = () => {
        this.releaseVoice(cue.id, voiceGroup)
        source.disconnect()
        cueGain.disconnect()
        panner?.disconnect()
      }
      source.start()
      return true
    } catch {
      // Missing/undecodable optional audio must never interrupt game flow.
      this.releaseVoice(cue.id, voiceGroup)
      this.bufferCache.delete(cue.src)
      return false
    }
  }

  private loadBuffer(src: string, context: AudioContext): Promise<AudioBuffer> {
    const cached = this.bufferCache.get(src)
    if (cached) return cached

    const request = this.fetchAudio(src)
      .then((response) => {
        if (!response.ok) throw new Error(`Audio request failed: ${response.status}`)
        return response.arrayBuffer()
      })
      .then((data) => context.decodeAudioData(data))
    this.bufferCache.set(src, request)
    return request
  }

  private playSynthNote(
    context: AudioContext,
    destination: AudioNode,
    options: {
      frequency: number
      gain: number
      startedAt: number
      duration: number
      type: OscillatorType
    },
  ): void {
    const oscillator = context.createOscillator()
    const envelope = context.createGain()
    oscillator.type = options.type
    oscillator.frequency.setValueAtTime(options.frequency, options.startedAt)
    envelope.gain.setValueAtTime(0.0001, options.startedAt)
    envelope.gain.exponentialRampToValueAtTime(options.gain, options.startedAt + 0.008)
    envelope.gain.exponentialRampToValueAtTime(
      0.0001,
      options.startedAt + options.duration - 0.01,
    )
    oscillator.connect(envelope)
    envelope.connect(destination)
    oscillator.start(options.startedAt)
    oscillator.stop(options.startedAt + options.duration)
  }

  private releaseVoice(id: string, voiceGroup: string): void {
    const remaining = Math.max(0, (this.activeVoices.get(id) ?? 1) - 1)
    if (remaining === 0) this.activeVoices.delete(id)
    else this.activeVoices.set(id, remaining)

    const groupRemaining = Math.max(0, (this.activeVoiceGroups.get(voiceGroup) ?? 1) - 1)
    if (groupRemaining === 0) this.activeVoiceGroups.delete(voiceGroup)
    else this.activeVoiceGroups.set(voiceGroup, groupRemaining)
    this.activeVoiceTotal = Math.max(0, this.activeVoiceTotal - 1)
  }
}
