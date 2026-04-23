import { describe, it, expect, vi, beforeEach } from 'vitest'
import { getBestiary } from './bestiary'

const fetchMock = vi.fn()
beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset()
})

describe('bestiary api', () => {
  it('getBestiary GETs /api/v1/bestiary and returns BestiaryDto', async () => {
    const body = {
      schemaVersion: 1,
      discoveredCardBaseIds: ['strike'],
      discoveredRelicIds: [],
      discoveredPotionIds: [],
      encounteredEnemyIds: [],
      allKnownCardBaseIds: ['strike', 'defend'],
      allKnownRelicIds: [],
      allKnownPotionIds: [],
      allKnownEnemyIds: [],
    }
    fetchMock.mockResolvedValue(new Response(JSON.stringify(body), { status: 200 }))

    const result = await getBestiary('alice')

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/v1/bestiary')
    expect(init.method).toBe('GET')
    expect(init.headers['X-Account-Id']).toBe('alice')
    expect(result).toEqual(body)
  })
})
