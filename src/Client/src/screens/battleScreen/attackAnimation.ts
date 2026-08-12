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
  nextStepIndex: number
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
        const damage = event.amount ?? 0
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

    // 構造化レスポンスの反応イベントは別スライドを作らない。最終状態は
    // response適用時に描画されるため、同じAllヒットの対象探索中は消費する。
    if (hitIndex !== null) {
      index++
      continue
    }

    break
  }

  return { casterId, hitIndex, targets, nextStepIndex: index }
}
