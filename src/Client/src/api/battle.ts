import { apiRequest } from './client'
import type { RunResultDto, RunSnapshotDto } from './types'

export async function winBattle(accountId: string, elapsedSeconds: number): Promise<RunSnapshotDto | RunResultDto> {
  return await apiRequest<RunSnapshotDto | RunResultDto>('POST', '/runs/current/battle/win', {
    accountId,
    body: { elapsedSeconds },
  })
}
