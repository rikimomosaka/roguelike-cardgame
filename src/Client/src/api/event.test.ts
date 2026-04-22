import { describe, it, expect, vi, beforeEach } from 'vitest'
import { getCurrentEvent, chooseEvent } from './event'

const fetchMock = vi.fn()
beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset()
})

describe('event api', () => {
  it('getCurrentEvent GETs /api/v1/event/current with accountId header', async () => {
    const eventPayload = {
      eventId: 'shrine',
      name: '祠',
      description: '古い祠を発見した。',
      choices: [
        { label: '祈る', conditionSummary: null, conditionMet: true },
        { label: '壊す', conditionSummary: null, conditionMet: true },
      ],
    }
    fetchMock.mockResolvedValue(new Response(JSON.stringify(eventPayload), { status: 200 }))
    const ev = await getCurrentEvent('alice')
    expect(fetchMock).toHaveBeenCalledTimes(1)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/v1/event/current')
    expect(init.method).toBe('GET')
    expect(init.headers['X-Account-Id']).toBe('alice')
    expect(ev.eventId).toBe('shrine')
    expect(ev.choices).toHaveLength(2)
  })

  it('chooseEvent POSTs choiceIndex', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }))
    await chooseEvent('alice', 1)
    const [url, init] = fetchMock.mock.calls[0]
    expect(url).toBe('/api/v1/event/choose')
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual({ choiceIndex: 1 })
    expect(init.headers['X-Account-Id']).toBe('alice')
  })
})
