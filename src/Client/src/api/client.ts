export class ApiError extends Error {
  readonly status: number
  readonly body: string

  constructor(status: number, body: string) {
    super(problemDetailsMessage(status, body))
    this.name = 'ApiError'
    this.status = status
    this.body = body
  }
}

const MAX_PROBLEM_TEXT_LENGTH = 500

function readableProblemText(value: unknown): string | null {
  if (typeof value !== 'string') return null
  const withoutControlCharacters = [...value]
    .map(character => {
      const code = character.charCodeAt(0)
      return code <= 0x1f || code === 0x7f ? ' ' : character
    })
    .join('')
  const normalized = withoutControlCharacters
    .replace(/\s+/g, ' ')
    .trim()
  if (normalized.length === 0) return null
  return normalized.slice(0, MAX_PROBLEM_TEXT_LENGTH)
}

/**
 * RFC 9457/7807 title/detail are safe to surface for client errors. Server
 * errors deliberately keep the generic status so internal details cannot leak.
 */
function problemDetailsMessage(status: number, body: string): string {
  const fallback = `HTTP ${status}`
  if (status < 400 || status >= 500 || body.trim().length === 0) return fallback

  try {
    const parsed: unknown = JSON.parse(body)
    if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return fallback
    const problem = parsed as Record<string, unknown>
    const title = readableProblemText(problem.title)
    const detail = readableProblemText(problem.detail)
    if (title && detail && title !== detail) return `${title}：${detail}`
    return title ?? detail ?? fallback
  } catch {
    return fallback
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
