import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { BGM_TRACKS, SE_CUES, selectSe } from '../audio/audioManifest'
import { AudioLabScreen } from './AudioLabScreen'

const audio = vi.hoisted(() => ({
  playBgm: vi.fn(),
  stopBgm: vi.fn(),
  playSe: vi.fn(async () => true),
  playComboTone: vi.fn<(combo: number) => boolean>(() => true),
}))

vi.mock('../audio', () => ({
  useAudio: () => ({
    settings: { schemaVersion: 2, master: 81, bgm: 62, se: 73, voice: 44 },
    ...audio,
  }),
}))

describe('AudioLabScreen', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('shows all audio entries and auditions BGM, SE, combo tones, and stop', () => {
    const onBack = vi.fn()
    render(<AudioLabScreen onBack={onBack} />)

    expect(screen.getByLabelText('現在の音量')).toHaveTextContent('MASTER81')
    expect(screen.getByLabelText('現在の音量')).toHaveTextContent('VOICE44')
    expect(screen.getByText(`${Object.keys(SE_CUES).length} cues`)).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /メニュー/ }))
    expect(audio.playBgm).toHaveBeenCalledWith(BGM_TRACKS.menu, { fadeMs: 300 })

    fireEvent.click(screen.getByRole('button', { name: '停止' }))
    expect(audio.stopBgm).toHaveBeenCalledWith({ fadeMs: 200 })

    fireEvent.click(screen.getByRole('button', { name: 'cardDraw' }))
    expect(audio.playSe).toHaveBeenCalledWith(selectSe('cardDraw', 1))

    fireEvent.click(screen.getByRole('button', { name: 'コンボ8を試聴' }))
    expect(audio.playComboTone).toHaveBeenCalledWith(8)

    fireEvent.click(screen.getByRole('button', { name: '戻る' }))
    expect(onBack).toHaveBeenCalledOnce()
  })

  it('auditions every combo tone from 1 through 15 in order', () => {
    vi.useFakeTimers()
    render(<AudioLabScreen onBack={() => undefined} />)

    fireEvent.click(screen.getByRole('button', { name: '連続再生' }))
    for (let step = 0; step < 15; step++) {
      act(() => { vi.advanceTimersByTime(180) })
    }

    expect(audio.playComboTone.mock.calls.map(([combo]) => combo)).toEqual(
      Array.from({ length: 15 }, (_, index) => index + 1),
    )
    expect(screen.getByRole('button', { name: '連続再生' })).toBeInTheDocument()
  })
})
