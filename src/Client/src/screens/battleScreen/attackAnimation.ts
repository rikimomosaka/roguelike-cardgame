import type { BattleEventStepDto } from '../../api/types'

export type AttackAnimationTarget = {
  targetId: string
  damage: number
  blockConsumed: number
}

export type AttackAnimationHit = {
  casterId: string | undefined
  hitIndex: number | null
  targets: AttackAnimationTarget[]
  /** Non-primary state changes emitted while this logical hit resolves. */
  stateChanges: AttackAnimationStateChange[]
  nextStepIndex: number
}

export type AttackAnimationStateChange = {
  targetId: string
  /** Positive heals, negative values are standalone damage. */
  hpDelta: number
  /** Positive gains Block; negative values consume it. */
  blockDelta: number
  /** Status mutations remain ordered with resource changes inside one hit. */
  statusId?: string
  statusDelta?: number
  statusRemoved?: boolean
  /** True when Core routes this damage through BlockPool.Consume. */
  consumesBlock?: boolean
  /** This change came from one BlockPool.Add call, even when amount <= 0. */
  gainsBlock?: boolean
  /** Attack gained from a reaction while this logical hit was resolving. */
  attackGainAmount?: number
  attackGainScope?: string | null
}

/** Apply the status-event contract used while an action is being animated. */
export function updateDisplayedDexterity(
  current: number,
  statusId: string | undefined,
  amount: number | undefined,
  removed: boolean | undefined,
): number {
  if (statusId !== 'dexterity') return current
  return removed ? 0 : current + (amount ?? 0)
}

export type AnimatedBlockState = {
  rawSum: number
  addCount: number
  dexterity: number
}

export function displayAnimatedBlock(state: AnimatedBlockState): number {
  return Math.max(0, state.rawSum + state.addCount * state.dexterity)
}

export function gainAnimatedBlock(
  state: AnimatedBlockState,
  rawAmount: number,
): AnimatedBlockState {
  return {
    ...state,
    rawSum: state.rawSum + rawAmount,
    addCount: state.addCount + 1,
  }
}

export function consumeAnimatedBlock(
  state: AnimatedBlockState,
  blockedAmount: number,
): AnimatedBlockState {
  return {
    ...state,
    rawSum: Math.max(0, displayAnimatedBlock(state) - Math.max(0, blockedAmount)),
    addCount: 0,
  }
}

type RectLike = Pick<DOMRect, 'left' | 'top' | 'width' | 'height'>

/** Convert a measured viewport rect to the canonical 1280x720 battle space. */
export function resolveBattleElementCenter(
  elementRect: RectLike | null | undefined,
  appRect: RectLike | null | undefined,
  fallback: { x: number; y: number },
): { x: number; y: number } {
  if (!elementRect || elementRect.width <= 0 || elementRect.height <= 0
      || !appRect || appRect.width <= 0 || appRect.height <= 0) {
    return fallback
  }
  return {
    x: (elementRect.left + elementRect.width / 2 - appRect.left) / (appRect.width / 1280),
    y: (elementRect.top + elementRect.height / 2 - appRect.top) / (appRect.height / 720),
  }
}

/**
 * AttackFire から始まる「論理上の1ヒット」を読み取る。
 *
 * All は同じ hitIndex を持つ AttackFire/DealDamage が対象数だけ並ぶため、
 * それらを1回のスライドと同時着弾へまとめる。Single/Random の次ヒットは
 * hitIndex が変わるので、次の呼び出しで別々に再生される。
 */
export function readAttackAnimationHit(
  steps: readonly BattleEventStepDto[],
  startStepIndex: number,
): AttackAnimationHit | null {
  const first = steps[startStepIndex]?.event
  if (!first || first.kind !== 'AttackFire') return null

  const casterId = first.casterInstanceId ?? undefined
  // Older saved/API responses do not contain hitIndex at all.  Normalize the
  // runtime `undefined` to the same legacy path as an explicit null so that
  // consecutive old AttackFire/DealDamage pairs remain separate hits.
  const hitIndex = first.hitIndex ?? null
  const targets: AttackAnimationTarget[] = []
  const stateChanges: AttackAnimationStateChange[] = []
  let pendingFireAmount = 0
  let pendingFireTargetId: string | undefined
  let consumedLegacyPair = false
  let index = startStepIndex

  while (index < steps.length) {
    const event = steps[index].event

    // hitIndex が無い旧レスポンスは隣接する1組だけを読む。
    // 新レスポンスでは、All対象間へOnHit・吸血・反射イベントが挟まっても、
    // 次のAttackFireまでは同じ論理ヒットとして走査する。
    const sameCaster = (event.casterInstanceId ?? undefined) === casterId
    const belongsToHit = sameCaster && (hitIndex === null
      ? !consumedLegacyPair
      : event.hitIndex === hitIndex)

    if (event.kind === 'AttackFire' && !belongsToHit) break
    if (index !== startStepIndex && (
      event.kind === 'BattleStart'
      || event.kind === 'TurnStart'
      || event.kind === 'EndTurn'
      || event.kind === 'BattleEnd'
      || event.kind === 'PlayCard'
      || event.kind === 'UsePotion'
      || event.kind === 'ReenactCardEffects'
      || event.kind === 'ContingencyTriggered'
      || event.kind === 'ContingencyResolved'
      // 次に行動する敵のカード名演出。ここで区切らないと次の敵の行動が
      // 前の攻撃ヒットに吸収され、カード名もバフ演出も出なくなる。
      || event.kind === 'EnemyCardAction'
    )) break

    if (event.kind === 'AttackFire' && belongsToHit) {
      if (hitIndex === null && index !== startStepIndex) break
      pendingFireAmount = event.amount ?? 0
      pendingFireTargetId = event.targetInstanceId ?? undefined
      index++
      continue
    }

    if (event.kind === 'DealDamage' && belongsToHit) {
      const targetId = event.targetInstanceId ?? undefined
      if (targetId) {
        const damage = event.actualHpLoss ?? event.amount ?? 0
        const legacyEstimate = pendingFireTargetId === targetId
          ? Math.max(0, pendingFireAmount - damage)
          : 0
        targets.push({
          targetId,
          damage,
          blockConsumed: event.blockedAmount ?? legacyEstimate,
        })
      }
      pendingFireAmount = 0
      pendingFireTargetId = undefined
      consumedLegacyPair = hitIndex === null
      index++
      continue
    }

    if (event.kind === 'CoverBlockSpent' && belongsToHit) {
      const targetId = event.targetInstanceId ?? undefined
      if (targetId) {
        targets.push({
          targetId,
          damage: 0,
          blockConsumed: Math.max(0, event.amount ?? 0),
        })
      }
      index++
      continue
    }

    if (event.kind === 'GainBlock') {
      const targetId = event.targetInstanceId ?? undefined
      if (targetId) {
        stateChanges.push({
          targetId,
          hpDelta: 0,
          blockDelta: event.amount ?? 0,
          gainsBlock: true,
        })
      }
      index++
      continue
    }

    if (event.kind === 'GainAttack') {
      const targetId = event.casterInstanceId
        ?? event.targetInstanceId
        ?? undefined
      if (targetId) {
        stateChanges.push({
          targetId,
          hpDelta: 0,
          blockDelta: 0,
          attackGainAmount: event.amount ?? 0,
          attackGainScope: event.note,
        })
      }
      index++
      continue
    }

    if (event.kind === 'Heal') {
      const targetId = event.targetInstanceId ?? undefined
      if (targetId) {
        stateChanges.push({
          targetId,
          hpDelta: Math.max(0, event.amount ?? 0),
          blockDelta: 0,
        })
      }
      index++
      continue
    }

    if ((event.kind === 'ApplyStatus' || event.kind === 'RemoveStatus')
        && event.targetInstanceId && event.note) {
      stateChanges.push({
        targetId: event.targetInstanceId,
        hpDelta: 0,
        blockDelta: 0,
        statusId: event.note,
        statusDelta: event.kind === 'ApplyStatus' ? (event.amount ?? 0) : undefined,
        statusRemoved: event.kind === 'RemoveStatus',
      })
      index++
      continue
    }

    // Reflected/self/status damage can be interleaved with a logical Attack
    // hit. It is not one of the primary targets above, but its HP change must
    // still become visible before the next hit starts.
    if (event.kind === 'DealDamage' && !belongsToHit) {
      const targetId = event.targetInstanceId ?? undefined
      if (targetId) {
        stateChanges.push({
          targetId,
          hpDelta: -Math.max(0, event.actualHpLoss ?? event.amount ?? 0),
          blockDelta: -Math.max(0, event.blockedAmount ?? 0),
          ...(event.damageType === 'Attack' ? { consumesBlock: true } : {}),
        })
      }
      index++
      continue
    }

    // 構造化レスポンスの反応イベントは別スライドを作らない。最終状態は
    // response適用時に描画されるため、同じAllヒットの対象探索中は消費する。
    if (hitIndex !== null) {
      index++
      continue
    }

    break
  }

  return { casterId, hitIndex, targets, stateChanges, nextStepIndex: index }
}
