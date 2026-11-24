import type { BattleEventDto } from '../../api/types'
import type { BattleSoundRecipeName } from '../../audio'

/** Semantic cue names. File selection and mixing belong to the audio manifest. */
export type BattleAudioCue =
  | 'battleStart'
  | 'turnStart'
  | 'turnEnd'
  | 'cardPlay'
  | 'cardDraw'
  | 'cardDiscard'
  | 'cardExhaust'
  | 'cardAdd'
  | 'cardRecover'
  | 'cardUpgrade'
  | 'cardShuffle'
  | 'attack'
  | 'hurt'
  | 'block'
  | 'gainBlock'
  | 'buff'
  | 'debuff'
  | 'heal'
  | 'poison'
  | 'energy'
  | 'death'
  | 'summon'
  | 'potion'
  | 'tactical'
  | 'ultimate'
  | 'contingencyArm'
  | 'contingencyTrigger'
  | 'reenact'
  | 'finisher'
  | 'trait'
  | 'ready'
  | 'resolution'
  | 'victory'
  | 'gameOver'

const DEBUFF_STATUS_IDS = new Set([
  'vulnerable',
  'weak',
  'poison',
  'handCostIncrease',
  'stagnation',
  'burn',
  'scorch',
  'confusion',
  'curse',
  'bind',
  'silence',
  'seal',
  'exhaustion',
  'distress',
  'disruption',
])

export function statusAudioCue(statusId: string | null, amount: number | null): 'buff' | 'debuff' {
  if ((statusId === 'strength' || statusId === 'dexterity') && (amount ?? 0) < 0) {
    return 'debuff'
  }
  return statusId && DEBUFF_STATUS_IDS.has(statusId) ? 'debuff' : 'buff'
}

/**
 * Convert a non-grouped Core event to sound cues.
 *
 * AttackFire and the state changes grouped into that logical hit are handled
 * by BattleScreen at the animation midpoint, so callers set groupedHit=true
 * to avoid playing the same damage/status cue twice.
 */
export function battleAudioCuesForEvent(
  event: BattleEventDto,
  options: { groupedHit?: boolean } = {},
): BattleAudioCue[] {
  const groupedHit = options.groupedHit === true
  switch (event.kind) {
    case 'BattleStart': return ['battleStart']
    case 'TurnStart': return ['turnStart']
    case 'EndTurn': return ['turnEnd']
    case 'PlayCard': return ['cardPlay']
    case 'AttackFire': return groupedHit ? [] : ['attack']
    case 'DealDamage': {
      if (groupedHit) return []
      const cues: BattleAudioCue[] = []
      if ((event.blockedAmount ?? 0) > 0) cues.push('block')
      if ((event.actualHpLoss ?? event.amount ?? 0) > 0) cues.push('hurt')
      return cues
    }
    case 'GainBlock': return groupedHit || (event.amount ?? 0) <= 0 ? [] : ['gainBlock']
    case 'GainAttack': return []
    case 'EnemyCardAction': return ['cardPlay']
    case 'ApplyStatus': return groupedHit ? [] : [statusAudioCue(event.note, event.amount)]
    case 'RemoveStatus': return []
    case 'PoisonTick': return ['poison']
    case 'Heal': return groupedHit || (event.amount ?? 0) <= 0 ? [] : ['heal']
    case 'Draw': return ['cardDraw']
    case 'Shuffle': return ['cardShuffle']
    case 'Discard': return ['cardDiscard']
    case 'Exhaust': return ['cardExhaust']
    case 'Upgrade': return ['cardUpgrade']
    case 'AddCard': return ['cardAdd']
    case 'RecoverFromDiscard':
    case 'RecoverFromExhaust': return ['cardRecover']
    case 'GainEnergy':
    case 'GainMaxEnergy': return ['energy']
    case 'ActorDeath': return ['death']
    case 'Summon': return ['summon']
    case 'UsePotion': return ['potion']
    case 'UseCharacterSkill': {
      const skillId = event.note?.toLowerCase()
      if (skillId === 'ultimate') return ['ultimate']
      if (skillId === 'tactical') return ['tactical']
      return ['tactical']
    }
    case 'ContingencyArmed': return ['contingencyArm']
    case 'ContingencyTriggered': return ['contingencyTrigger']
    case 'ContingencyResolved': {
      if (event.note === 'exhaust') return ['cardExhaust']
      if (event.note === 'discard') return ['cardDiscard']
      if (event.note === 'hand') return ['cardRecover']
      if (event.note === 'draw') return ['cardAdd']
      return []
    }
    case 'ReenactCardEffects': return ['reenact']
    case 'FinisherResolved': return ['finisher']
    case 'CoverBlockSpent': return groupedHit ? [] : ['block']
    case 'CharacterTraitTriggered': return ['trait']
    case 'ParticipantReadyChanged': return ['ready']
    case 'ResolutionStarted': return ['resolution']
    case 'BattleEnd': {
      const outcome = event.note?.toLowerCase()
      // GameOver belongs to App's run-result transition because defeat can
      // also originate outside battle (events or direct map HP loss).
      return outcome === 'victory' ? ['victory'] : []
    }
    case 'ComboGaugeChanged':
    case 'ModifyCardCost':
      return []
    default:
      return []
  }
}

/**
 * Preserve the real combo for milestone accents. BrowserAudioEngine clamps the
 * lead pitch itself, so 16+ still sounds at tier 15 without retriggering the
 * special 15-combo chord on every later card.
 */
export function comboToneTier(comboCountAfterPlay: number | null | undefined): number | null {
  if (comboCountAfterPlay === null || comboCountAfterPlay === undefined) return null
  return Math.max(1, Math.trunc(comboCountAfterPlay))
}

/**
 * Rich recipes replace (rather than accompany) the simple event cue. This is
 * kept separate from battleAudioCuesForEvent so replays and lightweight tests
 * can retain the old one-shot path.
 */
export function battleSoundRecipeForEvent(
  event: BattleEventDto,
  options: { groupedHit?: boolean } = {},
): BattleSoundRecipeName | null {
  if (options.groupedHit === true) return null
  switch (event.kind) {
    case 'DealDamage': {
      return battleContactSoundRecipe(
        event.blockedAmount ?? 0,
        event.actualHpLoss ?? event.amount ?? 0,
      )
    }
    case 'GainBlock': return (event.amount ?? 0) > 0 ? 'blockGain' : null
    case 'ApplyStatus': {
      if (event.note === 'poison') return 'poisonApplied'
      return statusAudioCue(event.note, event.amount) === 'debuff'
        ? 'debuffApplied'
        : 'buffApplied'
    }
    case 'Heal': return (event.amount ?? 0) > 0 ? 'healApplied' : null
    case 'FinisherResolved': return 'finisher'
    case 'ContingencyTriggered': return 'contingency'
    case 'ReenactCardEffects': return 'reenact'
    default: return null
  }
}

/** Select one contact stack for a grouped/multi-target AttackFire midpoint. */
export function battleContactSoundRecipe(
  totalBlockedAmount: number,
  totalHpLoss: number,
): BattleSoundRecipeName | null {
  const blocked = totalBlockedAmount > 0
  const hurt = totalHpLoss > 0
  if (blocked && hurt) return 'attackPartial'
  if (blocked) return 'attackBlocked'
  if (hurt) return 'attackHit'
  return null
}

export type BattleBgmDuckPreset = 'tactical' | 'ultimate' | 'finisher' | 'defeat'

export function bgmDuckPresetForEvent(event: BattleEventDto): BattleBgmDuckPreset | null {
  if (event.kind === 'FinisherResolved') return 'finisher'
  if (event.kind === 'UseCharacterSkill') {
    return event.note?.toLowerCase() === 'ultimate' ? 'ultimate' : 'tactical'
  }
  if (event.kind === 'BattleEnd' && event.note?.toLowerCase() !== 'victory') return 'defeat'
  return null
}
