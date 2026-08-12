import { apiRequest } from './client'
import type { EventInstanceDto, RunResultDto, RunSnapshotDto } from './types'

export async function getCurrentEvent(accountId: string): Promise<EventInstanceDto> {
  return apiRequest<EventInstanceDto>('GET', '/event/current', { accountId })
}

export async function chooseEvent(
  accountId: string,
  choiceIndex: number,
): Promise<RunSnapshotDto | RunResultDto> {
  return await apiRequest<RunSnapshotDto | RunResultDto>(
    'POST',
    '/event/choose',
    { accountId, body: { choiceIndex } },
  )
}
