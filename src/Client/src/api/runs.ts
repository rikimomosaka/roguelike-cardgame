import { ApiError, apiRequest } from './client'
import type { RunResultDto, RunSnapshotDto } from './types'

export async function getCurrentRun(accountId: string): Promise<RunSnapshotDto | null> {
  try {
    const result = await apiRequest<RunSnapshotDto | undefined>('GET', '/runs/current', { accountId })
    return result ?? null
  } catch (err) {
    if (err instanceof ApiError && err.status === 204) return null
    throw err
  }
}

export async function startNewRun(accountId: string, force = false): Promise<RunSnapshotDto> {
  const path = force ? '/runs/new?force=true' : '/runs/new'
  return await apiRequest<RunSnapshotDto>('POST', path, { accountId })
}

export async function moveToNode(accountId: string, nodeId: number, elapsedSeconds: number): Promise<void> {
  await apiRequest<void>('POST', '/runs/current/move', {
    accountId,
    body: { nodeId, elapsedSeconds },
  })
}

export async function abandonRun(accountId: string, elapsedSeconds: number): Promise<RunResultDto> {
  return await apiRequest<RunResultDto>('POST', '/runs/current/abandon', {
    accountId,
    body: { elapsedSeconds },
  })
}

export async function heartbeat(accountId: string, elapsedSeconds: number): Promise<void> {
  await apiRequest<void>('POST', '/runs/current/heartbeat', {
    accountId,
    body: { elapsedSeconds },
  })
}
