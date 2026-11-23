import {
  useCallback,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from 'react'
import {
  DEFAULT_PRESENTATION_SETTINGS,
  PRESENTATION_SETTINGS_STORAGE_KEY,
  getEffectiveMotionLevel,
  sanitizePresentationSettings,
  type PresentationSettings,
} from './presentationSettings'
import { PresentationContext, type PresentationContextValue } from './PresentationContext'

function readStoredSettings(): PresentationSettings {
  if (typeof window === 'undefined') return { ...DEFAULT_PRESENTATION_SETTINGS }
  try {
    const stored = window.localStorage.getItem(PRESENTATION_SETTINGS_STORAGE_KEY)
    return stored === null
      ? { ...DEFAULT_PRESENTATION_SETTINGS }
      : sanitizePresentationSettings(JSON.parse(stored) as unknown)
  } catch {
    return { ...DEFAULT_PRESENTATION_SETTINGS }
  }
}

function readSystemReducedMotion(): boolean {
  return typeof window !== 'undefined'
    && typeof window.matchMedia === 'function'
    && window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

export function PresentationProvider({ children }: { children: ReactNode }) {
  const [settings, setSettings] = useState<PresentationSettings>(readStoredSettings)
  const [systemReducedMotion, setSystemReducedMotion] = useState(readSystemReducedMotion)

  useEffect(() => {
    if (typeof window.matchMedia !== 'function') return
    const query = window.matchMedia('(prefers-reduced-motion: reduce)')
    const update = (event: MediaQueryListEvent | MediaQueryList) => {
      setSystemReducedMotion(event.matches)
    }
    update(query)
    if (typeof query.addEventListener === 'function') {
      query.addEventListener('change', update)
      return () => query.removeEventListener('change', update)
    }
    const legacyUpdate = (event: MediaQueryListEvent) => update(event)
    query.addListener(legacyUpdate)
    return () => query.removeListener(legacyUpdate)
  }, [])

  const effectiveMotionLevel = getEffectiveMotionLevel(
    settings.motionLevel,
    systemReducedMotion,
  )
  const screenShakeEnabled = settings.screenShake && effectiveMotionLevel === 'full'
  const flashesEnabled = settings.flashes && effectiveMotionLevel !== 'off'

  useEffect(() => {
    try {
      window.localStorage.setItem(
        PRESENTATION_SETTINGS_STORAGE_KEY,
        JSON.stringify(settings),
      )
    } catch {
      // Presentation preferences are optional. Private browsing and storage
      // policies must never prevent the game from starting.
    }
  }, [settings])

  useEffect(() => {
    const root = document.documentElement
    root.dataset.motion = effectiveMotionLevel
    root.dataset.shake = screenShakeEnabled ? 'on' : 'off'
    root.dataset.flash = flashesEnabled ? 'on' : 'off'
    root.dataset.uiScale = String(settings.uiScale)
    root.style.setProperty('--ui-scale-factor', String(settings.uiScale / 100))
  }, [effectiveMotionLevel, flashesEnabled, screenShakeEnabled, settings.uiScale])

  const updatePresentationSettings = useCallback((patch: Partial<PresentationSettings>) => {
    setSettings(current => sanitizePresentationSettings({ ...current, ...patch }))
  }, [])

  const resetPresentationSettings = useCallback(() => {
    setSettings({ ...DEFAULT_PRESENTATION_SETTINGS })
  }, [])

  const value = useMemo<PresentationContextValue>(() => ({
    settings,
    systemReducedMotion,
    effectiveMotionLevel,
    screenShakeEnabled,
    flashesEnabled,
    updatePresentationSettings,
    resetPresentationSettings,
  }), [
    effectiveMotionLevel,
    flashesEnabled,
    resetPresentationSettings,
    screenShakeEnabled,
    settings,
    systemReducedMotion,
    updatePresentationSettings,
  ])

  return (
    <PresentationContext.Provider value={value}>
      {children}
    </PresentationContext.Provider>
  )
}
