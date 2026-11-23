import { describe, expect, it } from 'vitest'
import type {
  BattleEventDto,
  BattleEventKind,
  BattleEventStepDto,
} from '../../api/types'
import {
  consumeAnimatedBlock,
  displayAnimatedBlock,
  gainAnimatedBlock,
  readAttackAnimationHit,
  resolveBattleElementCenter,
  updateDisplayedDexterity,
  type AnimatedBlockState,
} from './attackAnimation'

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
  it('applies Dexterity status events before later GainBlock display', () => {
    let state: AnimatedBlockState = { rawSum: 0, addCount: 0, dexterity: 1 }
    state = {
      ...state,
      dexterity: updateDisplayedDexterity(state.dexterity, 'dexterity', 2, false),
    }
    state = gainAnimatedBlock(state, 4)
    expect(displayAnimatedBlock(state)).toBe(7)
    state = {
      ...state,
      dexterity: updateDisplayedDexterity(state.dexterity, 'dexterity', undefined, true),
    }
    expect(displayAnimatedBlock(state)).toBe(4)
  })

  it('matches retroactive Dexterity, later Block.Add, and Consume semantics', () => {
    let state: AnimatedBlockState = { rawSum: 5, addCount: 1, dexterity: 0 }
    state = { ...state, dexterity: 2 }
    expect(displayAnimatedBlock(state)).toBe(7)
    state = gainAnimatedBlock(state, 4)
    expect(state.addCount).toBe(2)
    expect(displayAnimatedBlock(state)).toBe(13)
    state = consumeAnimatedBlock(state, 3)
    expect(state).toEqual({ rawSum: 10, addCount: 0, dexterity: 2 })
    expect(displayAnimatedBlock(state)).toBe(10)
    state = gainAnimatedBlock(state, 0)
    expect(state.addCount).toBe(1)
    expect(displayAnimatedBlock(state)).toBe(12)
  })

  it('maps measured pile centers to battle coordinates and falls back for zero rects', () => {
    expect(resolveBattleElementCenter(
      { left: 100, top: 50, width: 32, height: 64 },
      { left: 20, top: 10, width: 640, height: 360 },
      { x: 1, y: 2 },
    )).toEqual({ x: 192, y: 144 })
    expect(resolveBattleElementCenter(
      { left: 0, top: 0, width: 0, height: 0 },
      { left: 0, top: 0, width: 640, height: 360 },
      { x: 1216, y: 558 },
    )).toEqual({ x: 1216, y: 558 })
  })

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

  it('uses actual HP loss for overkill and keeps revive healing before the next hit', () => {
    const lethal = step('DealDamage', 'hero', 99, 0, 0)
    lethal.event.actualHpLoss = 7
    lethal.event.overkill = 92
    const removeRevive = step('RemoveStatus', 'hero', null, 0)
    removeRevive.event.note = 'revive'
    const reviveHeal = step('Heal', 'hero', 1, 0)
    const steps = [
      step('AttackFire', 'hero', 99, 0),
      lethal,
      removeRevive,
      reviveHeal,
      step('AttackFire', 'hero', 3, 1),
    ]

    const hit = readAttackAnimationHit(steps, 0)

    expect(hit?.targets[0].damage).toBe(7)
    expect(hit?.stateChanges).toEqual([
      {
        targetId: 'hero',
        hpDelta: 0,
        blockDelta: 0,
        statusId: 'revive',
        statusDelta: undefined,
        statusRemoved: true,
      },
      { targetId: 'hero', hpDelta: 1, blockDelta: 0 },
    ])
    expect(hit?.nextStepIndex).toBe(4)
  })

  it('attributes Cover block consumption to each actual provider', () => {
    const steps = [
      step('AttackFire', 'target', 10, 0),
      step('CoverBlockSpent', 'front-guard', 5, 0),
      step('CoverBlockSpent', 'rear-guard', 3, 0),
      step('DealDamage', 'target', 0, 0, 2),
    ]

    const hit = readAttackAnimationHit(steps, 0)

    expect(hit?.targets).toEqual([
      { targetId: 'front-guard', damage: 0, blockConsumed: 5 },
      { targetId: 'rear-guard', damage: 0, blockConsumed: 3 },
      { targetId: 'target', damage: 0, blockConsumed: 2 },
    ])
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

  it('stops before a reenacted card so its central popup is not consumed by the previous hit', () => {
    const steps = [
      step('AttackFire', 'enemy-a', 5, 0),
      step('DealDamage', 'enemy-a', 5, 0, 0),
      step('ReenactCardEffects', null, null, null),
      step('AttackFire', 'enemy-a', 6, 1),
      step('DealDamage', 'enemy-a', 6, 1, 0),
    ]

    const hit = readAttackAnimationHit(steps, 0)

    expect(hit?.nextStepIndex).toBe(2)
  })

  it('stops before Contingency completion so its pile movement is not consumed by the previous hit', () => {
    const steps = [
      step('AttackFire', 'enemy-a', 5, 0),
      step('DealDamage', 'enemy-a', 5, 0, 0),
      step('ContingencyResolved', null, null, null),
      step('AttackFire', 'enemy-a', 6, 1),
    ]

    expect(readAttackAnimationHit(steps, 0)?.nextStepIndex).toBe(2)
  })

  it('retains healing, gained block, and reflected damage interleaved with a hit', () => {
    const heal = step('Heal', 'hero', 3, 0)
    const block = step('GainBlock', 'hero', 4, 0)
    const reflected = step('DealDamage', 'hero', 2, 0, 1)
    reflected.event.casterInstanceId = 'enemy-a'
    reflected.event.actualHpLoss = 2
    const steps = [
      step('AttackFire', 'enemy-a', 5, 0),
      step('DealDamage', 'enemy-a', 5, 0, 0),
      heal,
      block,
      reflected,
      step('AttackFire', 'enemy-a', 5, 1),
    ]

    expect(readAttackAnimationHit(steps, 0)?.stateChanges).toEqual([
      { targetId: 'hero', hpDelta: 3, blockDelta: 0 },
      { targetId: 'hero', hpDelta: 0, blockDelta: 4, gainsBlock: true },
      { targetId: 'hero', hpDelta: -2, blockDelta: -1 },
    ])
  })

  it('retains an Attack gained by a reaction inside the logical hit', () => {
    const gainAttack = step('GainAttack', 'hero', 4, 0)
    gainAttack.event.casterInstanceId = 'hero'
    gainAttack.event.note = 'random;power:counter'
    const steps = [
      step('AttackFire', 'enemy-a', 5, 0),
      step('DealDamage', 'enemy-a', 5, 0, 0),
      gainAttack,
      step('AttackFire', 'enemy-a', 5, 1),
    ]

    expect(readAttackAnimationHit(steps, 0)?.stateChanges).toContainEqual({
      targetId: 'hero',
      hpDelta: 0,
      blockDelta: 0,
      attackGainAmount: 4,
      attackGainScope: 'random;power:counter',
    })
  })

  it('retains Dexterity mutations in event order around gained Block', () => {
    const dexterity = step('ApplyStatus', 'hero', 2, 0)
    dexterity.event.note = 'dexterity'
    const block = step('GainBlock', 'hero', 4, 0)
    const removeDexterity = step('RemoveStatus', 'hero', null, 0)
    removeDexterity.event.note = 'dexterity'
    const steps = [
      step('AttackFire', 'enemy-a', 5, 0),
      step('DealDamage', 'enemy-a', 5, 0, 0),
      dexterity,
      block,
      removeDexterity,
      step('AttackFire', 'enemy-a', 5, 1),
    ]

    expect(readAttackAnimationHit(steps, 0)?.stateChanges).toEqual([
      {
        targetId: 'hero', hpDelta: 0, blockDelta: 0,
        statusId: 'dexterity', statusDelta: 2, statusRemoved: false,
      },
      { targetId: 'hero', hpDelta: 0, blockDelta: 4, gainsBlock: true },
      {
        targetId: 'hero', hpDelta: 0, blockDelta: 0,
        statusId: 'dexterity', statusDelta: undefined, statusRemoved: true,
      },
    ])
  })
})
