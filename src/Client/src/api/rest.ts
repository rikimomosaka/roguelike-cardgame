import { apiRequest } from './client'

export async function restHeal(accountId: string): Promise<void> {
  await apiRequest<void>('POST', '/rest/heal', { accountId })
}

export async function restUpgrade(accountId: string, deckIndex: number): Promise<void> {
  await apiRequest<void>('POST', '/rest/upgrade', { accountId, body: { deckIndex } })
}
