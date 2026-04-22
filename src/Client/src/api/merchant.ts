import { apiRequest } from './client'
import type { MerchantInventoryDto } from './types'

export async function getMerchantInventory(accountId: string): Promise<MerchantInventoryDto> {
  return apiRequest<MerchantInventoryDto>('GET', '/merchant/inventory', { accountId })
}

export async function buyFromMerchant(
  accountId: string,
  body: { kind: 'card' | 'relic' | 'potion'; id: string },
): Promise<void> {
  await apiRequest<void>('POST', '/merchant/buy', { accountId, body })
}

export async function discardAtMerchant(accountId: string, deckIndex: number): Promise<void> {
  await apiRequest<void>('POST', '/merchant/discard', { accountId, body: { deckIndex } })
}

export async function leaveMerchant(accountId: string): Promise<void> {
  await apiRequest<void>('POST', '/merchant/leave', { accountId })
}
