import { describe, expect, it } from 'vitest'
import type {
  BattleEventDto,
  BattleEventKind,
  BattleEventStepDto,
} from '../../api/types'
import { readAttackAnimationHit } from './attackAnimation'

function step(
  kind: BattleEventKind,
  targetInstanceId: string | null,
  amount: number | null,
  hitIndex: number | null,
  blockedAmount: number | null = null,
): BattleEventStepDto {
  const event: BattleEventDto = {
    kind,
    order: 0,
    casterInstanceId: 'hero',
    targetInstanceId,
    amount,
    cardId: null,
    note: 'all',
    blockedAmount,
    hitIndex,
  }
  return { event, snapshotAfter: {} as BattleEventStepDto['snapshotAfter'] }
}

describe('readAttackAnimationHit', () => {
  it('groups every All target that shares one logical hit index', () => {
    const steps = [
      step('AttackFire', 'enemy-a', 5, 0),
      step('DealDamage', 'enemy-a', 5, 0, 0),
      step('AttackFire', 'enemy-b', 5, 0),
      step('DealDamage', 'enemy-b', 3, 0, 2),
      step('AttackFire', 'enemy-a', 5, 1),
      step('DealDamage', 'enemy-a', 5, 1, 0),
    ]

    const hit = readAttackAnimationHit(steps, 0)

    expect(hit?.nextStepIndex).toBe(4)
    expect(hit?.targets).toEqual([
      { targetId: 'enemy-a', damage: 5, blockConsumed: 0 },
      { targetId: 'enemy-b', damage: 3, blockConsumed: 2 },
    ])
  })

  it('uses explicit blockedAmount when Vulnerable makes damage exceed AttackFire', () => {
    const steps = [
      step('AttackFire', 'enemy-a', 10, 0),
      step('DealDamage', 'enemy-a', 13, 0, 1),
    ]

    const hit = readAttackAnimationHit(steps, 0)

    expect(hit?.targets[0]).toEqual({
      targetId: 'enemy-a',
      damage: 13,
      blockConsumed: 1,
    })
  })

  it('keeps the legacy subtraction only as a fallback for old responses', () => {
    const steps = [
      step('AttackFire', 'enemy-a', 10, null),
      step('DealDamage', 'enemy-a', 6, null),
    ]

    expect(readAttackAnimationHit(steps, 0)?.targets[0].blockConsumed).toBe(4)
  })

  it('treats an omitted legacy hitIndex as one AttackFire/DealDamage pair', () => {
    const firstAttack = step('AttackFire', 'enemy-a', 5, null)
    const firstDamage = step('DealDamage', 'enemy-a', 5, null)
    const secondAttack = step('AttackFire', 'enemy-a', 5, null)
    const secondDamage = step('DealDamage', 'enemy-a', 5, null)
    delete (firstAttack.event as Partial<BattleEventDto>).hitIndex
    delete (firstDamage.event as Partial<BattleEventDto>).hitIndex
    delete (secondAttack.event as Partial<BattleEventDto>).hitIndex
    delete (secondDamage.event as Partial<BattleEventDto>).hitIndex

    const steps = [firstAttack, firstDamage, secondAttack, secondDamage]
    const firstHit = readAttackAnimationHit(steps, 0)
    const secondHit = readAttackAnimationHit(steps, firstHit!.nextStepIndex)

    expect(firstHit?.hitIndex).toBeNull()
    expect(firstHit?.nextStepIndex).toBe(2)
    expect(firstHit?.targets).toHaveLength(1)
    expect(secondHit?.nextStepIndex).toBe(4)
    expect(secondHit?.targets).toHaveLength(1)
  })

  it('consumes ActorDeath inside an All hit before reading its next target', () => {
    const steps = [
      step('AttackFire', 'enemy-a', 5, 2),
      step('DealDamage', 'enemy-a', 5, 2, 0),
      step('ActorDeath', 'enemy-a', null, 2),
      step('AttackFire', 'enemy-b', 5, 2),
      step('DealDamage', 'enemy-b', 5, 2, 0),
    ]

    const hit = readAttackAnimationHit(steps, 0)

    expect(hit?.nextStepIndex).toBe(5)
    expect(hit?.targets.map(target => target.targetId)).toEqual(['enemy-a', 'enemy-b'])
  })

  it('groups All targets across OnHit and reflected reaction events', () => {
    const status = step('ApplyStatus', 'enemy-a', 1, null)
    const reflected = step('DealDamage', 'hero', 3, 4, 0)
    reflected.event.casterInstanceId = 'enemy-a'
    reflected.event.damageType = 'Reflected'
    const steps = [
      step('AttackFire', 'enemy-a', 5, 4),
      step('DealDamage', 'enemy-a', 5, 4, 0),
      status,
      reflected,
      step('AttackFire', 'enemy-b', 5, 4),
      step('DealDamage', 'enemy-b', 5, 4, 0),
      step('AttackFire', 'enemy-a', 5, 5),
      step('DealDamage', 'enemy-a', 5, 5, 0),
    ]

    const hit = readAttackAnimationHit(steps, 0)

    expect(hit?.nextStepIndex).toBe(6)
    expect(hit?.targets.map(target => target.targetId)).toEqual(['enemy-a', 'enemy-b'])
  })

  it('stops before the next turn even when this was the final attack hit', () => {
    const steps = [
      step('AttackFire', 'enemy-a', 5, 0),
      step('DealDamage', 'enemy-a', 5, 0, 0),
      step('Heal', 'hero', 1, 0),
      step('TurnStart', null, null, null),
      step('Draw', null, 5, null),
    ]

    const hit = readAttackAnimationHit(steps, 0)

    expect(hit?.nextStepIndex).toBe(3)
    expect(hit?.targets).toHaveLength(1)
  })
})
