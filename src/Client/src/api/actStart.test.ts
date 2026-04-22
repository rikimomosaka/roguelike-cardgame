import { describe, it, expect, vi, beforeEach } from 'vitest'
import { chooseActStartRelic } from './actStart'

const fetchMock = vi.fn()
beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset()
})

describe('actStart api', () => {
  it('chooseActStartRelic POSTs relicId to /api/v1/act-start/choose', async () => {
    const snapshot = { run: { progress: 'InProgress' }, map: {} }
    fetchMock.mockResolvedValue(new Response(JSON.stringify(snapshot), { status: 200 }))

    const result = await chooseActStartRelic('alice', 'relic-shield')

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/v1/act-start/choose')
    expect(init.method).toBe('POST')
    expect(init.headers['X-Account-Id']).toBe('alice')
    expect(JSON.parse(init.body as string)).toEqual({ relicId: 'relic-shield' })
    expect(result).toEqual(snapshot)
  })
})
