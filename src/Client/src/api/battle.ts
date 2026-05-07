import { apiRequest, ApiError } from './client'
import type {
  BattleActionResponseDto,
  BattleStateDto,
  RunResultDto,
  RunSnapshotDto,
} from './types'

// 既存 placeholder 用 (Phase 10.5 で削除予定)
export async function winBattle(accountId: string, elapsedSeconds: number): Promise<RunSnapshotDto | RunResultDto> {
  return await apiRequest<RunSnapshotDto | RunResultDto>('POST', '/runs/current/battle/win', {
    accountId,
    body: { elapsedSeconds },
  })
}

// ===== Phase 10.3-MVP Battle endpoints =====

export async function startBattle(accountId: string): Promise<BattleActionResponseDto> {
  return await apiRequest<BattleActionResponseDto>('POST', '/runs/current/battle/start', {
    accountId,
    body: {},
  })
}

export async function getBattle(accountId: string): Promise<BattleStateDto | null> {
  try {
    return await apiRequest<BattleStateDto>('GET', '/runs/current/battle', { accountId })
  } catch (e) {
    if (e instanceof ApiError && e.status === 404) return null
    throw e
  }
}

export async function playCard(
  accountId: string,
  body: { handIndex: number; targetEnemyIndex?: number; targetAllyIndex?: number },
): Promise<BattleActionResponseDto> {
  return await apiRequest<BattleActionResponseDto>('POST', '/runs/current/battle/play-card', {
    accountId,
    body,
  })
}

export async function endTurn(accountId: string): Promise<BattleActionResponseDto> {
  return await apiRequest<BattleActionResponseDto>('POST', '/runs/current/battle/end-turn', {
    accountId,
    body: {},
  })
}

export async function usePotion(
  accountId: string,
  body: { potionIndex: number; targetEnemyIndex?: number; targetAllyIndex?: number },
): Promise<BattleActionResponseDto> {
  return await apiRequest<BattleActionResponseDto>('POST', '/runs/current/battle/use-potion', {
    accountId,
    body,
  })
}

export async function setBattleTarget(
  accountId: string,
  body: { side: 'Ally' | 'Enemy'; slotIndex: number },
): Promise<BattleStateDto> {
  return await apiRequest<BattleStateDto>('POST', '/runs/current/battle/set-target', {
    accountId,
    body,
  })
}

export async function finalizeBattle(accountId: string): Promise<RunSnapshotDto | RunResultDto> {
  return await apiRequest<RunSnapshotDto | RunResultDto>('POST', '/runs/current/battle/finalize', {
    accountId,
    body: {},
  })
}

export async function resolveCardChoice(
  accountId: string,
  body: { selectedInstanceIds: string[] },
): Promise<BattleActionResponseDto> {
  return await apiRequest<BattleActionResponseDto>('POST', '/runs/current/battle/resolve-card-choice', {
    accountId,
    body,
  })
}
