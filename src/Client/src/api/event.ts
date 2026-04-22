import { apiRequest } from './client'
import type { EventInstanceDto } from './types'

export async function getCurrentEvent(accountId: string): Promise<EventInstanceDto> {
  return apiRequest<EventInstanceDto>('GET', '/event/current', { accountId })
}

export async function chooseEvent(accountId: string, choiceIndex: number): Promise<void> {
  await apiRequest<void>('POST', '/event/choose', { accountId, body: { choiceIndex } })
}
