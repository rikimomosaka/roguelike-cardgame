import { createContext, useContext } from 'react'
import {
  DEFAULT_PRESENTATION_SETTINGS,
  type MotionLevel,
  type PresentationSettings,
} from './presentationSettings'

export type PresentationContextValue = {
  settings: PresentationSettings
  systemReducedMotion: boolean
  effectiveMotionLevel: MotionLevel
  screenShakeEnabled: boolean
  flashesEnabled: boolean
  updatePresentationSettings: (patch: Partial<PresentationSettings>) => void
  resetPresentationSettings: () => void
}

export const PresentationContext = createContext<PresentationContextValue>({
  settings: DEFAULT_PRESENTATION_SETTINGS,
  systemReducedMotion: false,
  effectiveMotionLevel: DEFAULT_PRESENTATION_SETTINGS.motionLevel,
  screenShakeEnabled: DEFAULT_PRESENTATION_SETTINGS.screenShake,
  flashesEnabled: DEFAULT_PRESENTATION_SETTINGS.flashes,
  updatePresentationSettings: () => undefined,
  resetPresentationSettings: () => undefined,
})

export function usePresentationSettings(): PresentationContextValue {
  return useContext(PresentationContext)
}
