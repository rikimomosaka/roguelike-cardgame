import { describe, it, expect, vi, beforeEach } from 'vitest'
import {
  getMerchantInventory,
  buyFromMerchant,
  discardAtMerchant,
  leaveMerchant,
} from './merchant'

const fetchMock = vi.fn()
beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset()
})

describe('merchant api', () => {
  it('getMerchantInventory GETs /api/v1/merchant/inventory with accountId header', async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({
      cards: [], relics: [], potions: [], discardSlotUsed: false, discardPrice: 75,
    }), { status: 200 }))
    const inv = await getMerchantInventory('alice')
    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/v1/merchant/inventory')
    expect(init.method).toBe('GET')
    expect(init.headers['X-Account-Id']).toBe('alice')
    expect(inv.discardPrice).toBe(75)
  })

  it('buyFromMerchant POSTs kind+id', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }))
    await buyFromMerchant('alice', { kind: 'card', id: 'strike' })
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/v1/merchant/buy')
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({ kind: 'card', id: 'strike' })
  })

  it('discardAtMerchant POSTs deckIndex', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }))
    await discardAtMerchant('alice', 2)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/v1/merchant/discard')
    expect(JSON.parse(init.body as string)).toEqual({ deckIndex: 2 })
  })

  it('leaveMerchant POSTs with no body', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }))
    await leaveMerchant('alice')
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/v1/merchant/leave')
    expect(init.method).toBe('POST')
    expect(init.body).toBeUndefined()
  })
})
