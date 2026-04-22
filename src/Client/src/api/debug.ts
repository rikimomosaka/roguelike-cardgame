import { apiRequest } from './client'
import type { RunSnapshotDto, RunResultDto } from './types'

export async function applyDebugDamage(
  accountId: string,
  amount: number,
): Promise<RunSnapshotDto | RunResultDto> {
  return await apiRequest<RunSnapshotDto | RunResultDto>('POST', '/debug/damage', {
    accountId,
    body: { amount },
  })
}
