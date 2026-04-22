import { apiRequest } from './client'
import type { RunSnapshotDto } from './types'

export async function chooseActStartRelic(accountId: string, relicId: string): Promise<RunSnapshotDto> {
  return await apiRequest<RunSnapshotDto>('POST', '/act-start/choose', {
    accountId,
    body: { relicId },
  })
}
