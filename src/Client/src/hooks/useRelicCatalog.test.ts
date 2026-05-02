import { renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { resetRelicCatalogCacheForTests } from '../api/catalog'
import { useRelicCatalog } from './useRelicCatalog'

// Phase 10.5.L1.5: trigger は廃止 (catalog DTO から削除)。
const MOCK_RELICS = [
  {
    id: 'coin_purse',
    name: 'Coin Purse',
    description: 'Gain 20 gold on pickup.',
    rarity: 'Common',
  },
]

describe('useRelicCatalog', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    resetRelicCatalogCacheForTests()
    fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('fetches the relic catalog and returns catalog + names', async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify(MOCK_RELICS), { status: 200 }))

    const { result } = renderHook(() => useRelicCatalog())

    await waitFor(() => expect(result.current.catalog).not.toBeNull())

    expect(result.current.catalog!['coin_purse'].name).toBe('Coin Purse')
    expect(result.current.catalog!['coin_purse'].rarity).toBe('Common')
    expect(result.current.names['coin_purse']).toBe('Coin Purse')
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/v1/catalog/relics',
      expect.objectContaining({ method: 'GET' }),
    )
  })

  it('returns null catalog on fetch error', async () => {
    fetchMock.mockResolvedValue(new Response('Internal Server Error', { status: 500 }))

    const { result } = renderHook(() => useRelicCatalog())

    // Give it time to settle — catalog should remain null on error
    await new Promise((r) => setTimeout(r, 50))
    expect(result.current.catalog).toBeNull()
    expect(result.current.names).toEqual({})
  })
})
