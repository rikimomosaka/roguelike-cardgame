import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import type { AudioSettingsDto } from '../api/types'
import { useAccount } from '../context/AccountContext'
import {
  DEFAULT_AUDIO_SETTINGS,
  useAudioSettings,
  type AudioSettingsSaveStatus,
} from '../hooks/useAudioSettings'
import {
  BrowserAudioEngine,
  type AudioCue,
  type AudioEngine,
  type BgmDuckEnvelopeOptions,
  type BgmTrack,
  type BgmTransitionOptions,
  type LayeredAudioCue,
} from './audioEngine'

export type AudioContextValue = {
  settings: AudioSettingsDto
  settingsLoaded: boolean
  saveStatus: AudioSettingsSaveStatus
  updateSettings: (patch: Partial<Omit<AudioSettingsDto, 'schemaVersion'>>) => void
  flushSettings: () => Promise<void>
  playBgm: (track: BgmTrack, options?: BgmTransitionOptions) => void
  stopBgm: (options?: BgmTransitionOptions) => void
  setBgmDuck: (multiplier: number, rampMs?: number) => void
  duckBgm: (options?: BgmDuckEnvelopeOptions) => void
  playSe: (cue: AudioCue) => Promise<boolean>
  playLayeredSe: (cue: LayeredAudioCue) => Promise<number>
  playVoice: (cue: AudioCue) => Promise<boolean>
  playComboTone: (combo: number) => boolean
  unlock: () => Promise<void>
}

const NO_AUDIO_CONTEXT: AudioContextValue = {
  settings: DEFAULT_AUDIO_SETTINGS,
  settingsLoaded: true,
  saveStatus: 'idle',
  updateSettings: () => undefined,
  flushSettings: async () => undefined,
  playBgm: () => undefined,
  stopBgm: () => undefined,
  setBgmDuck: () => undefined,
  duckBgm: () => undefined,
  playSe: async () => false,
  playLayeredSe: async () => 0,
  playVoice: async () => false,
  playComboTone: () => false,
  unlock: async () => undefined,
}

// Audio is deliberately fail-soft: isolated screens, error boundaries, and
// tests remain playable even if they are rendered without the application
// provider. main.tsx always installs the real provider for the game.
const AudioContext = createContext<AudioContextValue>(NO_AUDIO_CONTEXT)

type AudioProviderProps = {
  children: ReactNode
  engine?: AudioEngine
}

export function AudioProvider({ children, engine: providedEngine }: AudioProviderProps) {
  const { accountId } = useAccount()
  const [engine] = useState<AudioEngine>(() => providedEngine ?? new BrowserAudioEngine())
  const destroyTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const {
    settings: persistedSettings,
    update,
    flush,
    saveStatus,
  } = useAudioSettings(accountId)
  const settings = persistedSettings ?? DEFAULT_AUDIO_SETTINGS

  useEffect(() => {
    engine.setSettings(settings)
  }, [engine, settings])

  useEffect(() => {
    if (destroyTimerRef.current !== null) {
      clearTimeout(destroyTimerRef.current)
      destroyTimerRef.current = null
    }
    const unlock = () => { void engine.unlock() }
    window.addEventListener('pointerdown', unlock, { capture: true })
    window.addEventListener('keydown', unlock, { capture: true })
    return () => {
      window.removeEventListener('pointerdown', unlock, { capture: true })
      window.removeEventListener('keydown', unlock, { capture: true })
      // React StrictMode immediately replays effects in development. Delaying
      // disposal one task lets that replay cancel destruction while still
      // releasing resources after a real provider unmount.
      destroyTimerRef.current = setTimeout(() => engine.destroy(), 0)
    }
  }, [engine])

  const playBgm = useCallback(
    (track: BgmTrack, options?: BgmTransitionOptions) => engine.playBgm(track, options),
    [engine],
  )
  const stopBgm = useCallback(
    (options?: BgmTransitionOptions) => engine.stopBgm(options),
    [engine],
  )
  const setBgmDuck = useCallback(
    (multiplier: number, rampMs?: number) => engine.setBgmDuck(multiplier, rampMs),
    [engine],
  )
  const duckBgm = useCallback(
    (options?: BgmDuckEnvelopeOptions) => engine.duckBgm(options),
    [engine],
  )
  const playSe = useCallback((cue: AudioCue) => engine.playSe(cue), [engine])
  const playLayeredSe = useCallback(
    (cue: LayeredAudioCue) => engine.playLayeredSe(cue),
    [engine],
  )
  const playVoice = useCallback((cue: AudioCue) => engine.playVoice(cue), [engine])
  const playComboTone = useCallback((combo: number) => engine.playComboTone(combo), [engine])
  const unlock = useCallback(() => engine.unlock(), [engine])
  const flushSettings = useCallback(() => flush({ keepalive: true }), [flush])

  const value = useMemo<AudioContextValue>(() => ({
    settings,
    settingsLoaded: persistedSettings !== null,
    saveStatus,
    updateSettings: update,
    flushSettings,
    playBgm,
    stopBgm,
    setBgmDuck,
    duckBgm,
    playSe,
    playLayeredSe,
    playVoice,
    playComboTone,
    unlock,
  }), [
    flushSettings,
    persistedSettings,
    duckBgm,
    playBgm,
    playComboTone,
    playLayeredSe,
    playSe,
    playVoice,
    saveStatus,
    setBgmDuck,
    settings,
    stopBgm,
    unlock,
    update,
  ])

  return <AudioContext.Provider value={value}>{children}</AudioContext.Provider>
}

// This hook intentionally shares the provider module so consumers use the same
// private context instance.
// eslint-disable-next-line react-refresh/only-export-components
export function useAudio(): AudioContextValue {
  return useContext(AudioContext)
}
