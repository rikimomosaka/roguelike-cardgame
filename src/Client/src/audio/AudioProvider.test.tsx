import { StrictMode, useEffect } from 'react'
import { act, render, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { AccountProvider } from '../context/AccountContext'
import { AudioProvider, useAudio } from './AudioProvider'
import type { AudioEngine } from './audioEngine'

class FakeEngine implements AudioEngine {
  setSettings = vi.fn()
  playBgm = vi.fn(() => undefined)
  stopBgm = vi.fn(() => undefined)
  setBgmDuck = vi.fn(() => undefined)
  duckBgm = vi.fn(() => undefined)
  playSe = vi.fn(async () => true)
  playLayeredSe = vi.fn(async () => 1)
  playVoice = vi.fn(async () => true)
  playComboTone = vi.fn(() => true)
  unlock = vi.fn(async () => undefined)
  destroy = vi.fn()
}

function Consumer() {
  const { playComboTone } = useAudio()
  useEffect(() => { playComboTone(3) }, [playComboTone])
  return null
}

describe('AudioProvider', () => {
  beforeEach(() => {
    window.localStorage.clear()
    vi.useFakeTimers({ shouldAdvanceTime: true })
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('survives StrictMode effect replay and destroys only after a real unmount', async () => {
    const engine = new FakeEngine()
    const view = render(
      <StrictMode>
        <AccountProvider>
          <AudioProvider engine={engine}>
            <Consumer />
          </AudioProvider>
        </AccountProvider>
      </StrictMode>,
    )

    await act(async () => { vi.advanceTimersByTime(0) })
    expect(engine.destroy).not.toHaveBeenCalled()

    act(() => window.dispatchEvent(new PointerEvent('pointerdown')))
    await waitFor(() => expect(engine.unlock).toHaveBeenCalled())

    view.unmount()
    await act(async () => { vi.advanceTimersByTime(0) })
    expect(engine.destroy).toHaveBeenCalledOnce()
  })
})
