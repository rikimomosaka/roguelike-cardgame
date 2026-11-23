import { fireEvent, render, screen, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { PresentationProvider } from '../presentation'
import { SettingsScreen } from './SettingsScreen'

vi.mock('../context/AccountContext', () => ({
  useAccount: () => ({ accountId: 'presentation-test' }),
}))

vi.mock('../audio', () => ({
  useAudio: () => ({
    settings: { master: 80, bgm: 70, se: 60, voice: 50 },
    settingsLoaded: true,
    updateSettings: vi.fn(),
    flushSettings: vi.fn(),
    saveStatus: 'idle',
  }),
}))

describe('SettingsScreen presentation controls', () => {
  beforeEach(() => {
    window.localStorage.clear()
    vi.stubGlobal('matchMedia', vi.fn(() => ({
      matches: false,
      media: '(prefers-reduced-motion: reduce)',
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    } as MediaQueryList)))
  })

  it('updates shake, flash, motion, and UI scale without changing audio settings', () => {
    render(
      <PresentationProvider>
        <SettingsScreen onBack={() => undefined} />
      </PresentationProvider>,
    )

    fireEvent.click(within(screen.getByRole('group', { name: '画面揺れ' }))
      .getByRole('button', { name: 'OFF' }))
    fireEvent.click(within(screen.getByRole('group', { name: '閃光表現' }))
      .getByRole('button', { name: 'OFF' }))
    fireEvent.click(within(screen.getByRole('group', { name: 'モーション量' }))
      .getByRole('button', { name: '軽減' }))
    fireEvent.click(within(screen.getByRole('group', { name: 'UIサイズ' }))
      .getByRole('button', { name: '110%' }))

    expect(document.documentElement.dataset.motion).toBe('reduced')
    expect(document.documentElement.dataset.shake).toBe('off')
    expect(document.documentElement.dataset.flash).toBe('off')
    expect(document.documentElement.dataset.uiScale).toBe('110')
  })
})
