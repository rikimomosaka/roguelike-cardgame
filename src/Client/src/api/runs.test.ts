import { afterEach, describe, expect, it, vi } from 'vitest'
import { startNewRun } from './runs'

const snapshot = {
  run: { progress: 'InProgress' },
  map: { startNodeId: 0, bossNodeId: 1, nodes: [] },
}

describe('startNewRun', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('keeps the legacy request body empty when no pick is supplied', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(snapshot), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)

    await startNewRun('alice')

    const [, init] = fetchMock.mock.calls[0]
    expect(init.body).toBeUndefined()
    expect(init.headers['X-Account-Id']).toBe('alice')
  })

  it('sends ordered character picks together with the force query', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify(snapshot), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)

    await startNewRun('alice', true, ['hero_a'])

    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/v1/runs/new?force=true')
    expect(JSON.parse(init.body)).toEqual({ characterIds: ['hero_a'] })
  })
})
