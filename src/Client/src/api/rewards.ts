import { apiRequest } from './client'
import type { RunSnapshotDto } from './types'

export async function claimGold(accountId: string): Promise<void> {
  await apiRequest<void>('POST', '/runs/current/reward/gold', { accountId })
}

export async function claimPotion(accountId: string): Promise<void> {
  await apiRequest<void>('POST', '/runs/current/reward/potion', { accountId })
}

export async function pickCard(accountId: string, cardId: string): Promise<void> {
  await apiRequest<void>('POST', '/runs/current/reward/card', {
    accountId,
    body: { cardId },
  })
}

export async function skipCard(accountId: string): Promise<void> {
  await apiRequest<void>('POST', '/runs/current/reward/card', {
    accountId,
    body: { skip: true },
  })
}

export async function proceedReward(accountId: string, elapsedSeconds: number): Promise<RunSnapshotDto> {
  return await apiRequest<RunSnapshotDto>('POST', '/runs/current/reward/proceed', {
    accountId,
    body: { elapsedSeconds },
  })
}

export async function discardPotion(accountId: string, slotIndex: number): Promise<void> {
  await apiRequest<void>('POST', '/runs/current/potion/discard', {
    accountId,
    body: { slotIndex },
  })
}

export async function claimRelic(accountId: string): Promise<void> {
  await apiRequest<void>('POST', '/runs/current/reward/claim-relic', { accountId })
}

// Phase 10.6.B T7: リロール — カード報酬を再抽選する (relic 所持時のみ有効)
export async function rerollCardChoices(accountId: string): Promise<RunSnapshotDto> {
  return await apiRequest<RunSnapshotDto>('POST', '/runs/current/reward/reroll-card-choices', {
    accountId,
  })
}
