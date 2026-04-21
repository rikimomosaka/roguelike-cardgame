import { ApiError, apiRequest } from './client'
import type { RunStateDto } from './types'

export async function getLatestRun(accountId: string): Promise<RunStateDto | null> {
  try {
    const result = await apiRequest<RunStateDto | undefined>('GET', '/runs/latest', { accountId })
    return result ?? null
  } catch (err) {
    if (err instanceof ApiError && err.status === 204) {
      return null
    }
    throw err
  }
}
