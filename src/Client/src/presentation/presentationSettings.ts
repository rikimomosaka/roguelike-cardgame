export const PRESENTATION_SETTINGS_STORAGE_KEY = 'roguelike-cardgame.presentation.v1'

export type MotionLevel = 'full' | 'reduced' | 'off'
export type UiScale = 90 | 100 | 110

export type PresentationSettings = {
  motionLevel: MotionLevel
  screenShake: boolean
  flashes: boolean
  uiScale: UiScale
}

export const DEFAULT_PRESENTATION_SETTINGS: PresentationSettings = {
  motionLevel: 'full',
  screenShake: true,
  flashes: true,
  uiScale: 100,
}

const motionLevels: readonly MotionLevel[] = ['full', 'reduced', 'off']
const uiScales: readonly UiScale[] = [90, 100, 110]

export function sanitizePresentationSettings(value: unknown): PresentationSettings {
  if (value === null || typeof value !== 'object') {
    return { ...DEFAULT_PRESENTATION_SETTINGS }
  }

  const candidate = value as Partial<PresentationSettings>
  return {
    motionLevel: motionLevels.includes(candidate.motionLevel as MotionLevel)
      ? candidate.motionLevel as MotionLevel
      : DEFAULT_PRESENTATION_SETTINGS.motionLevel,
    screenShake: typeof candidate.screenShake === 'boolean'
      ? candidate.screenShake
      : DEFAULT_PRESENTATION_SETTINGS.screenShake,
    flashes: typeof candidate.flashes === 'boolean'
      ? candidate.flashes
      : DEFAULT_PRESENTATION_SETTINGS.flashes,
    uiScale: uiScales.includes(candidate.uiScale as UiScale)
      ? candidate.uiScale as UiScale
      : DEFAULT_PRESENTATION_SETTINGS.uiScale,
  }
}

export function getEffectiveMotionLevel(
  requested: MotionLevel,
  systemReducedMotion: boolean,
): MotionLevel {
  if (requested === 'off') return 'off'
  return systemReducedMotion ? 'reduced' : requested
}
