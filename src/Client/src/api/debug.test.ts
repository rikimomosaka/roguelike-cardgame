import { describe, it, expect, vi, beforeEach } from 'vitest'
import { applyDebugDamage } from './debug'

const fetchMock = vi.fn()
beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset()
})

describe('debug api', () => {
  it('applyDebugDamage POSTs amount to /api/v1/debug/damage and returns snapshot when alive', async () => {
    const snapshot = { run: { progress: 'InProgress', currentHp: 50 }, map: {} }
    fetchMock.mockResolvedValue(new Response(JSON.stringify(snapshot), { status: 200 }))

    const result = await applyDebugDamage('alice', 10)

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/v1/debug/damage')
    expect(init.method).toBe('POST')
    expect(init.headers['X-Account-Id']).toBe('alice')
    expect(JSON.parse(init.body as string)).toEqual({ amount: 10 })
    expect(result).toEqual(snapshot)
  })

  it('applyDebugDamage returns RunResultDto when player dies', async () => {
    const resultDto = { outcome: 'GameOver', finalHp: 0, accountId: 'alice' }
    fetchMock.mockResolvedValue(new Response(JSON.stringify(resultDto), { status: 200 }))

    const result = await applyDebugDamage('alice', 9999)
    expect(result).toEqual(resultDto)
  })
})
