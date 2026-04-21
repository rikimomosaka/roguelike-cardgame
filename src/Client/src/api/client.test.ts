import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, apiRequest } from './client'

describe('apiRequest', () => {
  let fetchMock: ReturnType<typeof vi.fn>

  beforeEach(() => {
    fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('prefixes /api/v1 and returns parsed JSON', async () => {
    fetchMock.mockResolvedValue(new Response(JSON.stringify({ hello: 'world' }), { status: 200 }))
    const result = await apiRequest<{ hello: string }>('GET', '/ping')
    expect(fetchMock).toHaveBeenCalledWith('/api/v1/ping', expect.objectContaining({ method: 'GET' }))
    expect(result).toEqual({ hello: 'world' })
  })

  it('adds X-Account-Id header when provided', async () => {
    fetchMock.mockResolvedValue(new Response('null', { status: 200 }))
    await apiRequest('GET', '/whatever', { accountId: 'alice' })
    const init = fetchMock.mock.calls[0][1] as RequestInit
    const headers = new Headers(init.headers)
    expect(headers.get('X-Account-Id')).toBe('alice')
  })

  it('omits X-Account-Id when not provided', async () => {
    fetchMock.mockResolvedValue(new Response('null', { status: 200 }))
    await apiRequest('GET', '/whatever')
    const init = fetchMock.mock.calls[0][1] as RequestInit
    const headers = new Headers(init.headers)
    expect(headers.get('X-Account-Id')).toBeNull()
  })

  it('serializes body as JSON with Content-Type', async () => {
    fetchMock.mockResolvedValue(new Response('null', { status: 200 }))
    await apiRequest('POST', '/create', { body: { accountId: 'x' } })
    const init = fetchMock.mock.calls[0][1] as RequestInit
    const headers = new Headers(init.headers)
    expect(headers.get('Content-Type')).toBe('application/json')
    expect(init.body).toBe(JSON.stringify({ accountId: 'x' }))
  })

  it('returns undefined on 204 No Content', async () => {
    fetchMock.mockResolvedValue(new Response(null, { status: 204 }))
    const result = await apiRequest('PUT', '/void')
    expect(result).toBeUndefined()
  })

  it('throws ApiError carrying status and body on non-2xx', async () => {
    fetchMock.mockResolvedValue(new Response('boom', { status: 409 }))
    await expect(apiRequest('POST', '/dup')).rejects.toMatchObject({
      status: 409,
      body: 'boom',
    })
  })

  it('ApiError is instance of Error', async () => {
    fetchMock.mockResolvedValue(new Response('x', { status: 500 }))
    try {
      await apiRequest('GET', '/')
      expect.fail('should have thrown')
    } catch (e) {
      expect(e).toBeInstanceOf(ApiError)
      expect(e).toBeInstanceOf(Error)
    }
  })
})
