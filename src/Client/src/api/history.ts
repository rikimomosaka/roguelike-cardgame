import { apiRequest } from './client'
import type { RunResultDto } from './types'

export async function getLastResult(accountId: string): Promise<RunResultDto | null> {
  const result = await apiRequest<RunResultDto | undefined>('GET', '/history/last-result', { accountId })
  return result ?? null
}

export async function getHistory(accountId: string): Promise<RunResultDto[]> {
  return await apiRequest<RunResultDto[]>('GET', '/history', { accountId })
}
