import type {
  BattleActionResponseDto,
  BattleEventKind,
  BattleParticipantDto,
  BattleReplayEntryDto,
  BattleStateDto,
  CombatActorDto,
} from '../../api/types'

/** Battlefield placement is keyed by stable SlotIndex, never DTO array order. */
export function placeActorsBySlot<T extends { slotIndex: number }>(
  actors: readonly T[],
  slotCount: number,
): Array<T | null> {
  const slots: Array<T | null> = Array.from({ length: slotCount }, () => null)
  for (const actor of actors) {
    if (actor.slotIndex < 0 || actor.slotIndex >= slotCount) continue
    // Invalid duplicate slots retain the first authoritative actor rather than
    // visually moving subsequent actors to unrelated positions.
    if (slots[actor.slotIndex] === null) slots[actor.slotIndex] = actor
  }
  return slots
}

/**
 * Convert an immutable journal entry into the same presentation event stream
 * used by live battle actions. No game rule is executed here: every value and
 * both boundary states already came from the authoritative journal.
 */
export function replayEntryToActionResponse(
  entry: BattleReplayEntryDto,
  _stateBefore: BattleStateDto,
  stateAfter: BattleStateDto,
): BattleActionResponseDto {
  const defaultCasterId = entry.source?.actorInstanceId
    ?? entry.actorInstanceId
    ?? null
  return {
    state: snapshotBattleState(stateAfter),
    steps: entry.effects.map((effect, index) => ({
      event: {
        kind: effect.kind as BattleEventKind,
        order: index,
        casterInstanceId: effect.sourceActorInstanceId ?? defaultCasterId,
        targetInstanceId: effect.targetActorInstanceId,
        amount: effect.amount,
        cardId: effect.cardDefinitionId
          ?? effect.card?.definitionId
          ?? entry.card?.definitionId
          ?? null,
        note: effect.description,
        blockedAmount: effect.blockedAmount ?? null,
        hitIndex: effect.hitIndex ?? null,
        actualHpLoss: effect.actualHpLoss ?? null,
        hitLanded: effect.hitLanded ?? null,
        card: effect.card ?? null,
      },
      // Replay effects are presentation-only. The exact after-state is used
      // for side lookup and as the authoritative boundary after animation.
      snapshotAfter: snapshotBattleState(stateAfter),
    })),
  }
}

/**
 * Older saved battles expose only the root card state. The adapter keeps that
 * response usable while participant-aware servers expose the explicit owner.
 */
export function findPrimaryParticipant(
  state: BattleStateDto,
): BattleParticipantDto | null {
  const participants = state.participants ?? []
  if (participants.length === 0) return null

  const heroId = state.allies.find(actor => actor.definitionId === 'hero')?.instanceId
  return participants.find(participant => participant.actorInstanceId === heroId)
    ?? participants.find(participant => participant.controllerKind === 'Human')
    ?? participants[0]
}

export function findParticipantForActor(
  state: BattleStateDto,
  actorInstanceId: string,
): BattleParticipantDto | null {
  return (state.participants ?? []).find(
    participant => participant.actorInstanceId === actorInstanceId,
  ) ?? null
}

/** Freeze arrays/maps so live polling cannot mutate the replay presentation. */
export function snapshotParticipant(
  participant: BattleParticipantDto,
): BattleParticipantDto {
  return {
    ...participant,
    drawPile: participant.drawPile.slice(),
    hand: participant.hand.slice(),
    discardPile: participant.discardPile.slice(),
    exhaustPile: participant.exhaustPile.slice(),
    summonHeld: participant.summonHeld.slice(),
    powerCards: participant.powerCards.slice(),
    usedCharacterSkillIdsThisTurn: participant.usedCharacterSkillIdsThisTurn.slice(),
    characterSkillUsesThisTurn: { ...participant.characterSkillUsesThisTurn },
    cardUsesThisTurn: { ...participant.cardUsesThisTurn },
    ownedRelicIds: participant.ownedRelicIds.slice(),
    potions: participant.potions.slice(),
    potionUsesThisTurn: { ...participant.potionUsesThisTurn },
    finisherRecord: snapshotFinisherRecord(participant.finisherRecord),
  }
}

function snapshotFinisherRecord<T extends BattleStateDto['finisherRecord']>(
  record: T,
): T {
  if (!record) return record
  return {
    ...record,
    cards: record.cards.map(entry => ({
      ...entry,
      card: {
        ...entry.card,
        costChanges: entry.card.costChanges.map(change => ({ ...change })),
      },
    })),
    plannedAfterimageIntent: record.plannedAfterimageIntent
      ? { ...record.plannedAfterimageIntent }
      : null,
  } as T
}

export function snapshotActor(actor: CombatActorDto): CombatActorDto {
  return {
    ...actor,
    statuses: { ...actor.statuses },
    statusOrder: actor.statusOrder?.slice() ?? actor.statusOrder,
    intent: actor.intent ? { ...actor.intent } : actor.intent,
  }
}

export type ReplayPositionPresentation = {
  turn: number
  state: BattleStateDto
  participant: BattleParticipantDto | null
  actor: CombatActorDto | null
}

function replayPresentationFromState(
  turn: number,
  participantId: string,
  actorInstanceId: string,
  sourceState: BattleStateDto,
  fallbackParticipant: BattleParticipantDto | null = null,
  fallbackActor: CombatActorDto | null = null,
): ReplayPositionPresentation {
  // A replay remains anchored to the turn captured when it opened even when
  // a resolution snapshot already contains the next live turn.
  const state = { ...snapshotBattleState(sourceState), turn }
  const participant = (state.participants ?? []).find(candidate =>
    candidate.participantId === participantId)
    ?? findParticipantForActor(state, actorInstanceId)
    ?? (fallbackParticipant?.participantId === participantId
      ? snapshotParticipant(fallbackParticipant)
      : null)
  const actor = state.allies
    .concat(state.enemies)
    .find(candidate => candidate.instanceId === actorInstanceId)
    ?? (fallbackActor?.instanceId === actorInstanceId
      ? snapshotActor(fallbackActor)
      : null)
    ?? null
  return { turn, state, participant: participant ?? null, actor }
}

/** Deep-enough immutable copy of every mutable presentation collection. */
export function snapshotBattleState(state: BattleStateDto): BattleStateDto {
  return {
    ...state,
    allies: state.allies.map(snapshotActor),
    enemies: state.enemies.map(snapshotActor),
    drawPile: state.drawPile.slice(),
    hand: state.hand.slice(),
    discardPile: state.discardPile.slice(),
    exhaustPile: state.exhaustPile.slice(),
    summonHeld: state.summonHeld.slice(),
    powerCards: state.powerCards.slice(),
    usedCharacterSkillIdsThisTurn: state.usedCharacterSkillIdsThisTurn.slice(),
    characterSkillUsesThisTurn: state.characterSkillUsesThisTurn
      ? { ...state.characterSkillUsesThisTurn }
      : state.characterSkillUsesThisTurn,
    ownedRelicIds: state.ownedRelicIds.slice(),
    potions: state.potions.slice(),
    potionUsesThisTurn: state.potionUsesThisTurn
      ? { ...state.potionUsesThisTurn }
      : state.potionUsesThisTurn,
    pendingCardPlay: state.pendingCardPlay
      ? {
          ...state.pendingCardPlay,
          choice: {
            ...state.pendingCardPlay.choice,
            candidateInstanceIds:
              state.pendingCardPlay.choice.candidateInstanceIds.slice(),
          },
        }
      : null,
    participants: state.participants?.map(snapshotParticipant),
    finisherRecord: snapshotFinisherRecord(state.finisherRecord),
  }
}

/**
 * Builds the immutable presentation for one replay cursor position. Position
 * zero means "before the first visible action"; position N means the state
 * immediately after entry N. Missing snapshots fall back to the last known
 * replay state so old persisted journals remain viewable without leaking a
 * newer live battle DTO into a snapshot-aware timeline.
 */
export function replayPresentationAtPosition(
  turn: number,
  participantId: string,
  actorInstanceId: string,
  fallbackState: BattleStateDto,
  entries: readonly BattleReplayEntryDto[],
  position: number,
  fallbackParticipant: BattleParticipantDto | null = null,
  fallbackActor: CombatActorDto | null = null,
): ReplayPositionPresentation {
  const clampedPosition = Math.max(0, Math.min(position, entries.length))
  const firstStateBefore = entries.find(entry => entry.stateBefore)?.stateBefore
  let state = snapshotBattleState(firstStateBefore ?? fallbackState)

  for (let index = 0; index < clampedPosition; index++) {
    const entry = entries[index]
    if (entry.stateAfter) {
      state = snapshotBattleState(entry.stateAfter)
    } else if (entry.stateBefore && index === 0) {
      state = snapshotBattleState(entry.stateBefore)
    }
  }

  return replayPresentationFromState(
    turn,
    participantId,
    actorInstanceId,
    state,
    fallbackParticipant,
    fallbackActor,
  )
}

/**
 * Exact baseline for the action at a visible cursor position. A participant
 * filter can omit unrelated global sequences between two visible entries;
 * the later entry's authoritative StateBefore includes those changes. Paused
 * cursor semantics stay in replayPresentationAtPosition; this helper is used
 * only while the next action is actively being presented.
 */
export function replayPresentationAtEntryStart(
  turn: number,
  participantId: string,
  actorInstanceId: string,
  fallbackState: BattleStateDto,
  entries: readonly BattleReplayEntryDto[],
  position: number,
  fallbackParticipant: BattleParticipantDto | null = null,
  fallbackActor: CombatActorDto | null = null,
): ReplayPositionPresentation {
  const cursor = replayPresentationAtPosition(
    turn,
    participantId,
    actorInstanceId,
    fallbackState,
    entries,
    position,
    fallbackParticipant,
    fallbackActor,
  )
  const entry = entries[position]
  return entry?.stateBefore
    ? replayPresentationFromState(
        turn,
        participantId,
        actorInstanceId,
        entry.stateBefore,
        fallbackParticipant,
        fallbackActor,
      )
    : cursor
}

/**
 * Project a participant into the legacy root fields consumed by BattleScreen.
 * This is display-only: commands continue to use explicit participant ids.
 */
export function projectParticipantForDisplay(
  state: BattleStateDto,
  participant: BattleParticipantDto | null,
): BattleStateDto {
  if (!participant) return state
  return {
    ...state,
    targetAllyIndex: participant.targetAllyIndex,
    targetEnemyIndex: participant.targetEnemyIndex,
    energy: participant.energy,
    energyPerTurn: participant.energyPerTurn,
    drawPile: participant.drawPile,
    hand: participant.hand,
    discardPile: participant.discardPile,
    exhaustPile: participant.exhaustPile,
    summonHeld: participant.summonHeld,
    powerCards: participant.powerCards,
    comboCount: participant.comboCount,
    maxComboThisTurn: participant.maxComboThisTurn,
    comboGauge: participant.comboGauge,
    usedCharacterSkillIdsThisTurn: participant.usedCharacterSkillIdsThisTurn,
    characterSkillUsesThisTurn: participant.characterSkillUsesThisTurn,
    lastPlayedOrigCost: participant.lastPlayedOrigCost,
    nextCardComboFreePass: participant.nextCardComboFreePass,
    comboCarryoverPending: participant.comboCarryoverPending
      ?? state.comboCarryoverPending
      ?? false,
    ownedRelicIds: participant.ownedRelicIds,
    potions: participant.potions,
    potionUsesThisTurn: participant.potionUsesThisTurn,
    pendingCardPlay: participant.pendingCardPlay,
    characterSkillsRestricted: participant.characterSkillsRestricted
      ?? state.characterSkillsRestricted,
    finisherRecord: participant.finisherRecord ?? null,
    playHistoryThisTurn: participant.playHistoryThisTurn ?? [],
  }
}
