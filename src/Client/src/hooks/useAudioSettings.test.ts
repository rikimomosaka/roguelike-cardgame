import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useAudioSettings } from './useAudioSettings'

const DEFAULT_SETTINGS = {
  schemaVersion: 1,
  master: 80,
  bgm: 70,
  se: 80,
  ambient: 60,
}

describe('useAudioSettings', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    vi.useFakeTimers({ shouldAdvanceTime: true })
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.unstubAllGlobals()
  })

  it('fetches settings on mount', async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify(DEFAULT_SETTINGS), { status: 200 }))
    const { result } = renderHook(() => useAudioSettings('alice'))

    await waitFor(() => expect(result.current.settings).not.toBeNull())
    expect(result.current.settings?.master).toBe(80)
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/audio-settings',
      expect.objectContaining({ method: 'GET' }),
    )
  })

  it('update() applies optimistic change and PUTs after 500ms', async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify(DEFAULT_SETTINGS), { status: 200 }),
    )
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))

    const { result } = renderHook(() => useAudioSettings('alice'))
    await waitFor(() => expect(result.current.settings).not.toBeNull())

    act(() => {
      result.current.update({ master: 10 })
    })
    expect(result.current.settings?.master).toBe(10)
    expect(fetchMock).toHaveBeenCalledTimes(1)

    await act(async () => {
      vi.advanceTimersByTime(500)
    })

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
    const putCall = fetchMock.mock.calls[1][1] as RequestInit
    expect(putCall.method).toBe('PUT')
  })

  it('debounces multiple rapid updates into single PUT', async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify(DEFAULT_SETTINGS), { status: 200 }),
    )
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 204 }))

    const { result } = renderHook(() => useAudioSettings('alice'))
    await waitFor(() => expect(result.current.settings).not.toBeNull())

    act(() => {
      result.current.update({ master: 10 })
    })
    act(() => {
      vi.advanceTimersByTime(200)
    })
    act(() => {
      result.current.update({ master: 20 })
    })
    act(() => {
      vi.advanceTimersByTime(200)
    })
    act(() => {
      result.current.update({ master: 30 })
    })
    await act(async () => {
      vi.advanceTimersByTime(500)
    })

    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
    const putBody = JSON.parse((fetchMock.mock.calls[1][1] as RequestInit).body as string)
    expect(putBody.master).toBe(30)
  })

  it('sets saveStatus to error when PUT fails', async () => {
    fetchMock.mockResolvedValueOnce(
      new Response(JSON.stringify(DEFAULT_SETTINGS), { status: 200 }),
    )
    fetchMock.mockResolvedValueOnce(new Response('boom', { status: 500 }))

    const { result } = renderHook(() => useAudioSettings('alice'))
    await waitFor(() => expect(result.current.settings).not.toBeNull())

    act(() => {
      result.current.update({ master: 10 })
    })
    await act(async () => {
      vi.advanceTimersByTime(500)
    })

    await waitFor(() => expect(result.current.saveStatus).toBe('error'))
  })
})
