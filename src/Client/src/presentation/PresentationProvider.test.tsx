import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  PresentationProvider,
} from './PresentationProvider'
import { usePresentationSettings } from './PresentationContext'
import { PRESENTATION_SETTINGS_STORAGE_KEY } from './presentationSettings'

function Probe() {
  const {
    settings,
    effectiveMotionLevel,
    screenShakeEnabled,
    updatePresentationSettings,
  } = usePresentationSettings()
  return (
    <div>
      <output aria-label="requested motion">{settings.motionLevel}</output>
      <output aria-label="effective motion">{effectiveMotionLevel}</output>
      <output aria-label="effective shake">{String(screenShakeEnabled)}</output>
      <button
        type="button"
        onClick={() => updatePresentationSettings({
          motionLevel: 'off',
          screenShake: false,
          uiScale: 110,
        })}
      >
        update
      </button>
    </div>
  )
}

function mockReducedMotion(matches: boolean) {
  const listeners = new Set<(event: MediaQueryListEvent) => void>()
  const query = {
    matches,
    media: '(prefers-reduced-motion: reduce)',
    onchange: null,
    addListener: vi.fn(),
    removeListener: vi.fn(),
    addEventListener: vi.fn((_name: string, listener: (event: MediaQueryListEvent) => void) => {
      listeners.add(listener)
    }),
    removeEventListener: vi.fn((_name: string, listener: (event: MediaQueryListEvent) => void) => {
      listeners.delete(listener)
    }),
    dispatchEvent: vi.fn(),
  } as unknown as MediaQueryList
  vi.stubGlobal('matchMedia', vi.fn(() => query))
}

describe('PresentationProvider', () => {
  beforeEach(() => {
    window.localStorage.clear()
    mockReducedMotion(false)
    delete document.documentElement.dataset.motion
    delete document.documentElement.dataset.shake
    delete document.documentElement.dataset.flash
    delete document.documentElement.dataset.uiScale
    document.documentElement.style.removeProperty('--ui-scale-factor')
  })

  it('applies and persists settings as presentation data attributes', async () => {
    render(<PresentationProvider><Probe /></PresentationProvider>)

    fireEvent.click(screen.getByRole('button', { name: 'update' }))

    await waitFor(() => expect(document.documentElement.dataset.motion).toBe('off'))
    expect(document.documentElement.dataset.shake).toBe('off')
    expect(document.documentElement.dataset.uiScale).toBe('110')
    expect(document.documentElement.style.getPropertyValue('--ui-scale-factor')).toBe('1.1')
    expect(JSON.parse(window.localStorage.getItem(PRESENTATION_SETTINGS_STORAGE_KEY) ?? '{}'))
      .toMatchObject({ motionLevel: 'off', screenShake: false, uiScale: 110 })
  })

  it('forces effective reduced motion and disables shake when requested by the OS', async () => {
    mockReducedMotion(true)
    render(<PresentationProvider><Probe /></PresentationProvider>)

    expect(screen.getByLabelText('requested motion')).toHaveTextContent('full')
    expect(screen.getByLabelText('effective motion')).toHaveTextContent('reduced')
    expect(screen.getByLabelText('effective shake')).toHaveTextContent('false')
    await waitFor(() => expect(document.documentElement.dataset.motion).toBe('reduced'))
    expect(document.documentElement.dataset.shake).toBe('off')
  })
})
