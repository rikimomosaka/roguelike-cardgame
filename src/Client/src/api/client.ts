export class ApiError extends Error {
  readonly status: number
  readonly body: string

  constructor(status: number, body: string) {
    super(`HTTP ${status}`)
    this.name = 'ApiError'
    this.status = status
    this.body = body
  }
}

export type ApiRequestOptions = {
  accountId?: string
  body?: unknown
}

export async function apiRequest<T>(
  method: string,
  path: string,
  opts?: ApiRequestOptions,
): Promise<T> {
  const headers: Record<string, string> = {
    'Content-Type': 'application/json',
  }
  if (opts?.accountId) {
    headers['X-Account-Id'] = opts.accountId
  }

  const response = await fetch(`/api/v1${path}`, {
    method,
    headers,
    body: opts?.body !== undefined ? JSON.stringify(opts.body) : undefined,
  })

  if (!response.ok) {
    const text = await response.text()
    throw new ApiError(response.status, text)
  }

  if (response.status === 204) {
    return undefined as T
  }

  return (await response.json()) as T
}
