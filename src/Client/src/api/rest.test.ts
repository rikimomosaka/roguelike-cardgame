import { describe, it, expect, vi, beforeEach } from 'vitest'
import { restHeal, restUpgrade } from './rest'

const fetchMock = vi.fn()
beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset()
})

describe('rest api', () => {
  it('restHeal POSTs /api/v1/rest/heal with no body', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }))
    await restHeal('alice')
    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/v1/rest/heal')
    expect(init.method).toBe('POST')
    expect(init.headers['X-Account-Id']).toBe('alice')
    expect(init.body).toBeUndefined()
  })

  it('restUpgrade POSTs deckIndex to /api/v1/rest/upgrade', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }))
    await restUpgrade('alice', 3)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/v1/rest/upgrade')
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({ deckIndex: 3 })
    expect(init.headers['X-Account-Id']).toBe('alice')
  })
})
