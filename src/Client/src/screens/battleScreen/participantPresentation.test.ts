import { describe, expect, it } from 'vitest'
import type {
  BattleParticipantDto,
  BattleReplayEntryDto,
  BattleStateDto,
  CombatActorDto,
} from '../../api/types'
import {
  findParticipantForActor,
  findPrimaryParticipant,
  placeActorsBySlot,
  projectParticipantForDisplay,
  replayEntryToActionResponse,
  replayPresentationAtEntryStart,
  replayPresentationAtPosition,
  snapshotParticipant,
} from './participantPresentation'

const card = (instanceId: string) => ({
  instanceId,
  cardDefinitionId: 'card',
  isUpgraded: false,
  originalCost: 1,
  currentCost: 1,
  costOverride: null,
  adjustedDescription: null,
  adjustedUpgradedDescription: null,
})

function participant(overrides: Partial<BattleParticipantDto> = {}): BattleParticipantDto {
  return {
    participantId: 'p1',
    actorInstanceId: 'hero-1',
    controllerKind: 'Human',
    ownerPlayerSlot: 0,
    characterId: 'default',
    targetAllyIndex: 0,
    targetEnemyIndex: 1,
    energy: 7,
    energyPerTurn: 3,
    drawPerTurn: 5,
    drawPile: [card('draw')],
    hand: [card('hand')],
    discardPile: [],
    exhaustPile: [],
    summonHeld: [],
    powerCards: [],
    comboCount: 2,
    maxComboThisTurn: 4,
    comboGauge: 6,
    lastPlayedOrigCost: 1,
    nextCardComboFreePass: false,
    wildUsedInCurrentCombo: false,
    usedCharacterSkillIdsThisTurn: [],
    characterSkillUsesThisTurn: {},
    cardUsesThisTurn: {},
    cardUseLockedThisTurn: false,
    ownedRelicIds: [],
    potions: [],
    potionUsesThisTurn: {},
    pendingCardPlay: null,
    cpuCardPlaysThisTurn: 0,
    isReady: false,
    ...overrides,
  }
}

const state = {
  allies: [
    { instanceId: 'hero-1', definitionId: 'hero' },
    { instanceId: 'wisp-1', definitionId: 'wisp' },
  ],
  participants: [
    participant(),
    participant({ participantId: 'cpu-1', actorInstanceId: 'wisp-1', controllerKind: 'Cpu' }),
  ],
  energy: 3,
  hand: [],
} as unknown as BattleStateDto

function replayActor(
  attackSingle: number | null,
  attackHits: number | null,
): CombatActorDto {
  return {
    instanceId: 'wisp-1',
    definitionId: 'wisp',
    side: 'Ally',
    slotIndex: 1,
    currentHp: 30,
    maxHp: 30,
    blockDisplay: 0,
    attackSingleDisplay: attackSingle ?? 0,
    attackRandomDisplay: 0,
    attackAllDisplay: 0,
    statuses: {},
    currentMoveId: null,
    remainingLifetimeTurns: null,
    associatedSummonHeldInstanceId: null,
    intent: {
      attackSingle,
      attackRandom: null,
      attackAll: null,
      attackHits,
      attackSingleHits: attackHits,
      attackRandomHits: null,
      attackAllHits: null,
      block: null,
      hasBuff: false,
      hasDebuff: false,
      hasHeal: false,
    },
  }
}

function replayBattleState(
  owner: BattleParticipantDto,
  ownerActor: CombatActorDto,
  enemies: CombatActorDto[] = [],
): BattleStateDto {
  return {
    turn: 4,
    phase: 'PlayerInput',
    outcome: 'Pending',
    allies: [ownerActor],
    enemies,
    targetAllyIndex: owner.targetAllyIndex,
    targetEnemyIndex: enemies.length > 0 ? 0 : null,
    energy: owner.energy,
    energyPerTurn: owner.energyPerTurn,
    drawPile: owner.drawPile,
    hand: owner.hand,
    discardPile: owner.discardPile,
    exhaustPile: owner.exhaustPile,
    summonHeld: owner.summonHeld,
    powerCards: owner.powerCards,
    comboCount: owner.comboCount,
    maxComboThisTurn: owner.maxComboThisTurn,
    comboGauge: owner.comboGauge,
    usedCharacterSkillIdsThisTurn: owner.usedCharacterSkillIdsThisTurn,
    characterSkillUsesThisTurn: owner.characterSkillUsesThisTurn,
    lastPlayedOrigCost: owner.lastPlayedOrigCost,
    nextCardComboFreePass: owner.nextCardComboFreePass,
    ownedRelicIds: owner.ownedRelicIds,
    potions: owner.potions,
    potionUsesThisTurn: owner.potionUsesThisTurn,
    encounterId: 'replay-test',
    pendingCardPlay: owner.pendingCardPlay,
    participants: [owner],
  }
}

function replayEntry(overrides: Partial<BattleReplayEntryDto>): BattleReplayEntryDto {
  return {
    sequence: 1,
    turn: 4,
    commandId: 'cpu-action',
    kind: 'playCard',
    viewKind: 'action',
    participantId: 'cpu-1',
    actorInstanceId: 'wisp-1',
    revisionBefore: 0,
    revisionAfter: 1,
    card: null,
    targetActorInstanceIds: [],
    effects: [],
    intents: [],
    feedChanges: [],
    isResolution: false,
    note: null,
    ...overrides,
  }
}

describe('participantPresentation', () => {
  it('keeps visual gaps and inserts a re-summoned actor by stable slot index', () => {
    const hero = { instanceId: 'hero', slotIndex: 0 }
    const olderSummon = { instanceId: 'slot-2', slotIndex: 2 }
    const reSummoned = { instanceId: 'slot-1-new', slotIndex: 1 }

    // DTO array order is command/creation order and deliberately differs from
    // battlefield placement after the middle summon was removed and replaced.
    expect(placeActorsBySlot([hero, olderSummon], 4)).toEqual([
      hero, null, olderSummon, null,
    ])
    expect(placeActorsBySlot([hero, olderSummon, reSummoned], 4)).toEqual([
      hero, reSummoned, olderSummon, null,
    ])
  })

  it('adapts journal effects to the live presentation event contract without recalculation', () => {
    const owner = participant({ participantId: 'cpu-1', actorInstanceId: 'wisp-1' })
    const before = replayBattleState(owner, replayActor(null, null), [
      { ...replayActor(null, null), instanceId: 'enemy-1', side: 'Enemy', slotIndex: 0 },
    ])
    const after = replayBattleState(owner, replayActor(null, null), [
      {
        ...replayActor(null, null),
        instanceId: 'enemy-1',
        side: 'Enemy',
        slotIndex: 0,
        currentHp: 23,
      },
    ])
    const response = replayEntryToActionResponse(replayEntry({
      actorInstanceId: 'wisp-1',
      effects: [
        {
          kind: 'AttackFire',
          sourceActorInstanceId: 'wisp-1',
          targetParticipantId: null,
          targetActorInstanceId: 'enemy-1',
          amount: 9,
          statusId: null,
          description: null,
          hitIndex: 0,
          hitLanded: true,
        },
        {
          kind: 'DealDamage',
          sourceActorInstanceId: 'wisp-1',
          targetParticipantId: null,
          targetActorInstanceId: 'enemy-1',
          amount: 7,
          statusId: null,
          description: null,
          actualHpLoss: 7,
          blockedAmount: 2,
          hitIndex: 0,
          hitLanded: true,
        },
      ],
    }), before, after)

    expect(response.state.enemies[0].currentHp).toBe(23)
    expect(response.steps.map(step => step.event.kind)).toEqual(['AttackFire', 'DealDamage'])
    expect(response.steps[1].event).toEqual(expect.objectContaining({
      casterInstanceId: 'wisp-1',
      targetInstanceId: 'enemy-1',
      amount: 7,
      actualHpLoss: 7,
      blockedAmount: 2,
      hitIndex: 0,
      hitLanded: true,
    }))
  })

  it('keeps a reenacted card snapshot on its presentation-only event', () => {
    const owner = participant({ participantId: 'cpu-1', actorInstanceId: 'wisp-1' })
    const replayState = replayBattleState(owner, replayActor(null, null))
    const response = replayEntryToActionResponse(replayEntry({
      actorInstanceId: 'wisp-1',
      effects: [{
        kind: 'ReenactCardEffects',
        sourceActorInstanceId: 'wisp-1',
        targetParticipantId: null,
        targetActorInstanceId: null,
        amount: null,
        statusId: null,
        description: '再演',
        cardDefinitionId: 'source-card',
        cardInstanceId: 'source-card-1',
        card: {
          cardInstanceId: 'source-card-1',
          definitionId: 'source-card',
          displayName: '再演元',
          cardType: 'Attack',
          rarity: 'Rare',
          isUpgraded: true,
          originalCost: 2,
          currentCost: 2,
          paidCost: 0,
          description: '敵単体に12アタック。',
          costChanges: [],
          poolClass: 'Striker',
        },
      }],
    }), replayState, replayState)

    expect(response.steps).toHaveLength(1)
    expect(response.steps[0].event).toEqual(expect.objectContaining({
      kind: 'ReenactCardEffects',
      cardId: 'source-card',
      card: expect.objectContaining({
        cardInstanceId: 'source-card-1',
        isUpgraded: true,
        poolClass: 'Striker',
      }),
    }))
  })

  it('finds primary and actor-owned participant deterministically', () => {
    expect(findPrimaryParticipant(state)?.participantId).toBe('p1')
    expect(findParticipantForActor(state, 'wisp-1')?.participantId).toBe('cpu-1')
  })

  it('projects independent piles, energy and combo into the legacy display shape', () => {
    const cpu = {
      ...state.participants![1],
      comboCarryoverPending: true,
    }
    const view = projectParticipantForDisplay(state, cpu)
    expect(view.energy).toBe(7)
    expect(view.hand[0].instanceId).toBe('hand')
    expect(view.comboCount).toBe(2)
    expect(view.targetEnemyIndex).toBe(1)
    expect(view.comboCarryoverPending).toBe(true)
  })

  it('falls back to the root carryover marker for an older participant DTO', () => {
    const legacyState = {
      ...state,
      comboCarryoverPending: true,
    }

    expect(projectParticipantForDisplay(
      legacyState,
      state.participants![1],
    ).comboCarryoverPending).toBe(true)
  })

  it('takes an immutable presentation copy of mutable DTO collections', () => {
    const original = participant()
    const copy = snapshotParticipant(original)
    original.hand.push(card('late'))
    original.characterSkillUsesThisTurn.tactical = 1
    expect(copy.hand).toHaveLength(1)
    expect(copy.characterSkillUsesThisTurn).toEqual({})
  })

  it('advances hand, energy, combo and intent from each decision-time snapshot', () => {
    const before = participant({
      participantId: 'cpu-1',
      actorInstanceId: 'wisp-1',
      controllerKind: 'Cpu',
      energy: 2,
      hand: [card('first'), card('second')],
      comboCount: 0,
      maxComboThisTurn: 0,
    })
    const afterFirst = participant({
      ...before,
      energy: 1,
      hand: [card('second')],
      discardPile: [card('first')],
      comboCount: 1,
      maxComboThisTurn: 1,
    })
    const afterSecond = participant({
      ...afterFirst,
      energy: 0,
      hand: [],
      discardPile: [card('first'), card('second')],
      comboCount: 2,
      maxComboThisTurn: 2,
    })
    const actorBefore = replayActor(null, null)
    const actorAfterFirst = replayActor(3, 1)
    const actorAfterSecond = replayActor(7, 2)
    const stateBefore = replayBattleState(before, actorBefore)
    const stateAfterFirst = replayBattleState(afterFirst, actorAfterFirst)
    const stateAfterSecond = replayBattleState(afterSecond, actorAfterSecond)
    const entries = [
      replayEntry({
        stateBefore,
        stateAfter: stateAfterFirst,
      }),
      replayEntry({
        sequence: 2,
        commandId: 'cpu-action-2',
        revisionBefore: 1,
        revisionAfter: 2,
        stateBefore: stateAfterFirst,
        stateAfter: stateAfterSecond,
      }),
    ]

    // The fallback deliberately contains the final state. Position zero must
    // still use the first decision's before snapshot instead of leaking it.
    const atStart = replayPresentationAtPosition(
      4, 'cpu-1', 'wisp-1', stateAfterSecond, entries, 0,
    )
    const afterOne = replayPresentationAtPosition(
      4, 'cpu-1', 'wisp-1', stateAfterSecond, entries, 1,
    )
    const afterTwo = replayPresentationAtPosition(
      4, 'cpu-1', 'wisp-1', stateAfterSecond, entries, 2,
    )

    expect(atStart.turn).toBe(4)
    expect(atStart.participant!.energy).toBe(2)
    expect(atStart.participant!.hand.map(item => item.instanceId)).toEqual(['first', 'second'])
    expect(atStart.participant!.comboCount).toBe(0)
    expect(atStart.actor!.intent?.attackSingle).toBeNull()

    expect(afterOne.participant!.energy).toBe(1)
    expect(afterOne.participant!.hand.map(item => item.instanceId)).toEqual(['second'])
    expect(afterOne.participant!.comboCount).toBe(1)
    expect(afterOne.actor!.intent?.attackSingle).toBe(3)

    expect(afterTwo.participant!.energy).toBe(0)
    expect(afterTwo.participant!.hand).toEqual([])
    expect(afterTwo.participant!.comboCount).toBe(2)
    expect(afterTwo.actor!.intent?.attackSingle).toBe(7)
  })

  it('uses the next visible entry StateBefore when global sequence gaps changed actors', () => {
    const owner = participant({ participantId: 'cpu-1', actorInstanceId: 'wisp-1' })
    const enemyAtFirstAfter = {
      ...replayActor(null, null),
      instanceId: 'enemy-1',
      definitionId: 'enemy',
      side: 'Enemy' as const,
      slotIndex: 0,
      currentHp: 30,
      maxHp: 30,
    }
    const enemyAfterHiddenSequence = { ...enemyAtFirstAfter, currentHp: 12 }
    const firstAfter = replayBattleState(owner, replayActor(null, null), [enemyAtFirstAfter])
    const secondBefore = replayBattleState(owner, replayActor(null, null), [
      enemyAfterHiddenSequence,
    ])
    const secondAfter = replayBattleState(owner, replayActor(null, null), [])
    const entries = [
      replayEntry({
        sequence: 1,
        stateBefore: firstAfter,
        stateAfter: firstAfter,
      }),
      replayEntry({
        sequence: 3,
        commandId: 'visible-after-hidden-sequence',
        stateBefore: secondBefore,
        stateAfter: secondAfter,
      }),
    ]

    // Pausing after visible sequence 1 keeps its exact after-state.
    const paused = replayPresentationAtPosition(
      4, 'cpu-1', 'wisp-1', secondAfter, entries, 1,
    )
    expect(paused.state.enemies[0].currentHp).toBe(30)

    // Starting visible sequence 3 first applies its StateBefore, which already
    // includes the filtered-out global sequence 2 damage.
    const startingSecond = replayPresentationAtEntryStart(
      4, 'cpu-1', 'wisp-1', secondAfter, entries, 1,
    )
    expect(startingSecond.state.enemies[0].currentHp).toBe(12)
  })

  it('retains the opened subject snapshots after resolution removes its actor and participant', () => {
    const owner = participant({
      participantId: 'cpu-1',
      actorInstanceId: 'wisp-1',
      controllerKind: 'Cpu',
      energy: 8,
    })
    const ownerActor = replayActor(null, null)
    const before = replayBattleState(owner, ownerActor)
    const after = {
      ...before,
      allies: [],
      participants: [],
    }
    const entries = [replayEntry({ stateBefore: before, stateAfter: after })]

    const finalView = replayPresentationAtPosition(
      4,
      owner.participantId,
      owner.actorInstanceId,
      before,
      entries,
      1,
      owner,
      ownerActor,
    )
    expect(finalView.state.allies).toEqual([])
    expect(finalView.participant).toEqual(expect.objectContaining({
      participantId: 'cpu-1',
      energy: 8,
    }))
    expect(finalView.actor).toEqual(expect.objectContaining({
      instanceId: 'wisp-1',
      definitionId: 'wisp',
    }))
  })

  it('keeps the turn captured when replay opens even if a newer live turn exists', () => {
    const capturedTurn = 6
    const newerLiveTurn = 7
    const view = replayPresentationAtPosition(
      capturedTurn,
      'p1',
      'hero-1',
      replayBattleState(
        participant(),
        { ...replayActor(null, null), instanceId: 'hero-1', definitionId: 'hero' },
      ),
      [],
      0,
    )

    expect(newerLiveTurn).not.toBe(view.turn)
    expect(view.turn).toBe(capturedTurn)
  })

  it('uses the full battle snapshot so a later-dead target is alive at an earlier position', () => {
    const owner = participant({
      participantId: 'cpu-1',
      actorInstanceId: 'wisp-1',
      controllerKind: 'Cpu',
    })
    const ownerActor = replayActor(null, null)
    const livingEnemy: CombatActorDto = {
      ...replayActor(null, null),
      instanceId: 'enemy-1',
      definitionId: 'slime',
      side: 'Enemy',
      slotIndex: 0,
      currentHp: 8,
      maxHp: 8,
    }
    const before = replayBattleState(owner, ownerActor, [livingEnemy])
    const after = replayBattleState(owner, ownerActor, [])
    const entries = [replayEntry({ stateBefore: before, stateAfter: after })]

    const atStart = replayPresentationAtPosition(
      4, owner.participantId, owner.actorInstanceId, after, entries, 0,
    )
    const atEnd = replayPresentationAtPosition(
      4, owner.participantId, owner.actorInstanceId, after, entries, 1,
    )

    expect(atStart.state.enemies).toHaveLength(1)
    expect(atStart.state.enemies[0]).toMatchObject({ instanceId: 'enemy-1', currentHp: 8 })
    expect(atStart.state.targetEnemyIndex).toBe(0)
    expect(atEnd.state.enemies).toEqual([])
  })
})
