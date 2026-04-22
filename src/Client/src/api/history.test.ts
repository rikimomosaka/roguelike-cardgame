import { describe, it, expect, vi, beforeEach } from 'vitest'
import { getLastResult, getHistory } from './history'

const fetchMock = vi.fn()
beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset()
})

describe('history api', () => {
  it('getLastResult GETs /api/v1/history/last-result and returns RunResultDto', async () => {
    const rec = { outcome: 'Cleared', accountId: 'alice', runId: 'r1' }
    fetchMock.mockResolvedValue(new Response(JSON.stringify(rec), { status: 200 }))

    const result = await getLastResult('alice')

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/v1/history/last-result')
    expect(init.method).toBe('GET')
    expect(init.headers['X-Account-Id']).toBe('alice')
    expect(result).toEqual(rec)
  })

  it('getLastResult returns null on 204 No Content', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }))

    const result = await getLastResult('alice')
    expect(result).toBeNull()
  })

  it('getHistory GETs /api/v1/history and returns RunResultDto array', async () => {
    const list = [{ outcome: 'Cleared', accountId: 'alice', runId: 'r1' }]
    fetchMock.mockResolvedValue(new Response(JSON.stringify(list), { status: 200 }))

    const result = await getHistory('alice')

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/v1/history')
    expect(init.method).toBe('GET')
    expect(init.headers['X-Account-Id']).toBe('alice')
    expect(result).toEqual(list)
  })
})
