import type { BattleEventStepDto } from '../../api/types'

function belongsToDisplayedActor(
  step: BattleEventStepDto,
  actorInstanceId: string | undefined,
): boolean {
  if (!actorInstanceId) return true
  const target = step.event.targetInstanceId
  return target === null || target === undefined || target === actorInstanceId
}

/**
 * A hand identity diff alone is not a draw signal: card recovery, choice
 * resolution and participant projection can all add a hand DOM node. Base
 * turn refresh predates explicit Draw events, so TurnStart remains the sole
 * compatibility trigger for that authoritative hand addition.
 */
export function hasDrawPresentationTrigger(
  steps: readonly BattleEventStepDto[],
  actorInstanceId?: string,
): boolean {
  return steps.some(step => (
    (step.event.kind === 'Draw' && (step.event.amount ?? 0) > 0)
    || step.event.kind === 'TurnStart'
  ) && belongsToDisplayedActor(step, actorInstanceId))
}

/** Only a real Core Shuffle event may move cards from discard to draw. */
export function hasShufflePresentationTrigger(
  steps: readonly BattleEventStepDto[],
  actorInstanceId?: string,
): boolean {
  return steps.some(step => step.event.kind === 'Shuffle'
    && (step.event.amount ?? 0) > 0
    && belongsToDisplayedActor(step, actorInstanceId))
}
