import { renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { resetEventCatalogCacheForTests } from '../api/catalog'
import { useEventCatalog } from './useEventCatalog'

const MOCK_EVENTS = [
  {
    id: 'ancient_shrine',
    name: 'Ancient Shrine',
    description: 'A mysterious shrine glows with faint light.',
    choices: [
      {
        label: 'Offer gold',
        conditionSummary: 'requires 30 gold',
        effectSummaries: ['Lose 30 gold', 'Gain a rare relic'],
      },
      {
        label: 'Leave',
        conditionSummary: null,
        effectSummaries: ['Nothing happens'],
      },
    ],
  },
]

describe('useEventCatalog', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    resetEventCatalogCacheForTests()
    fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('fetches the event catalog and returns catalog + names', async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify(MOCK_EVENTS), { status: 200 }))

    const { result } = renderHook(() => useEventCatalog())

    await waitFor(() => expect(result.current.catalog).not.toBeNull())

    expect(result.current.catalog!['ancient_shrine'].name).toBe('Ancient Shrine')
    expect(result.current.catalog!['ancient_shrine'].choices).toHaveLength(2)
    expect(result.current.catalog!['ancient_shrine'].choices[0].effectSummaries).toEqual([
      'Lose 30 gold',
      'Gain a rare relic',
    ])
    expect(result.current.catalog!['ancient_shrine'].choices[1].conditionSummary).toBeNull()
    expect(result.current.names['ancient_shrine']).toBe('Ancient Shrine')
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/catalog/events',
      expect.objectContaining({ method: 'GET' }),
    )
  })

  it('returns null catalog on fetch error', async () => {
    fetchMock.mockResolvedValue(new Response('Internal Server Error', { status: 500 }))

    const { result } = renderHook(() => useEventCatalog())

    // Give it time to settle — catalog should remain null on error
    await new Promise((r) => setTimeout(r, 50))
    expect(result.current.catalog).toBeNull()
    expect(result.current.names).toEqual({})
  })
})
