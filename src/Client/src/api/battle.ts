import { apiRequest, ApiError } from './client'
import type {
  BattleActionResponseDto,
  BattleReplayDto,
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

export type BattleCommandContext = {
  participantId?: string
  actorInstanceId?: string
  commandId?: string
  expectedRevision?: number
}

let fallbackCommandSequence = 0

/** Stable per-attempt identity used by the Server to reject duplicate/stale commands. */
export function createBattleCommandId(kind: string): string {
  const uuid = globalThis.crypto?.randomUUID?.()
  if (uuid) return `${kind}:${uuid}`
  fallbackCommandSequence += 1
  return `${kind}:${Date.now().toString(36)}:${fallbackCommandSequence.toString(36)}`
}

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
  body: {
    handIndex: number
    targetEnemyIndex?: number
    targetAllyIndex?: number
    cardInstanceId?: string
  } & BattleCommandContext,
): Promise<BattleActionResponseDto> {
  return await apiRequest<BattleActionResponseDto>('POST', '/runs/current/battle/play-card', {
    accountId,
    body,
  })
}

export async function endTurn(
  accountId: string,
  body: BattleCommandContext & { commandId: string; expectedRevision: number },
): Promise<BattleActionResponseDto> {
  return await apiRequest<BattleActionResponseDto>('POST', '/runs/current/battle/end-turn', {
    accountId,
    body,
  })
}

/** Toggle Planning readiness. Resolution starts atomically when every active participant is ready. */
export async function setBattleReady(
  accountId: string,
  body: { participantId: string; isReady: boolean } & BattleCommandContext,
): Promise<BattleActionResponseDto> {
  return await apiRequest<BattleActionResponseDto>('POST', '/runs/current/battle/set-ready', {
    accountId,
    body,
  })
}

export type BattleReplayQuery = {
  participantId?: string
  actorInstanceId?: string
  turn?: number
  afterSequence?: number
  throughSequence?: number
}

/** Read-only append-only action feed used by ally and future multiplayer replay. */
export async function getBattleReplay(
  accountId: string,
  query: BattleReplayQuery = {},
): Promise<BattleReplayDto> {
  const search = new URLSearchParams()
  if (query.participantId) search.set('participantId', query.participantId)
  if (query.actorInstanceId) search.set('actorInstanceId', query.actorInstanceId)
  if (query.turn !== undefined) search.set('turn', String(query.turn))
  if (query.afterSequence !== undefined) search.set('afterSequence', String(query.afterSequence))
  if (query.throughSequence !== undefined) search.set('throughSequence', String(query.throughSequence))
  const suffix = search.size > 0 ? `?${search.toString()}` : ''
  return await apiRequest<BattleReplayDto>('GET', `/runs/current/battle/replay${suffix}`, {
    accountId,
  })
}

export async function usePotion(
  accountId: string,
  body: {
    potionIndex: number
    targetEnemyIndex?: number
    targetAllyIndex?: number
    potionId?: string
  } & BattleCommandContext,
): Promise<BattleActionResponseDto> {
  return await apiRequest<BattleActionResponseDto>('POST', '/runs/current/battle/use-potion', {
    accountId,
    body,
  })
}

export async function useTacticalSkill(
  accountId: string,
  context: BattleCommandContext = {},
): Promise<BattleActionResponseDto> {
  return await apiRequest<BattleActionResponseDto>('POST', '/runs/current/battle/use-tactical-skill', {
    accountId,
    body: context,
  })
}

export async function useUltimateSkill(
  accountId: string,
  context: BattleCommandContext = {},
): Promise<BattleActionResponseDto> {
  return await apiRequest<BattleActionResponseDto>('POST', '/runs/current/battle/use-ultimate-skill', {
    accountId,
    body: context,
  })
}

export async function useCharacterSkill(
  accountId: string,
  skillId: string,
  context: BattleCommandContext = {},
): Promise<BattleActionResponseDto> {
  return await apiRequest<BattleActionResponseDto>('POST', '/runs/current/battle/use-character-skill', {
    accountId,
    body: { skillId, ...context },
  })
}

export async function setBattleTarget(
  accountId: string,
  body: { side: 'Ally' | 'Enemy'; slotIndex: number } & BattleCommandContext,
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
  body: {
    selectedInstanceIds: string[]
    pendingCardInstanceId?: string
  } & BattleCommandContext,
): Promise<BattleActionResponseDto> {
  return await apiRequest<BattleActionResponseDto>('POST', '/runs/current/battle/resolve-card-choice', {
    accountId,
    body,
  })
}
