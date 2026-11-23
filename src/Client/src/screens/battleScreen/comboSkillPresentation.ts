import type { BattlePhase } from '../../api/types'

export const COMBO_GAUGE_MAX = 10
export const TACTICAL_SKILL_COST = 3
export const ULTIMATE_SKILL_COST = 6

export const TACTICAL_SKILL_DESCRIPTION =
  '自身に筋力1を付与。\n自身に拡散1を付与。'
export const ULTIMATE_SKILL_DESCRIPTION =
  '3枚引く。\n自身に節制3を付与。'
export const COMBO_GAUGE_DESCRIPTION =
  'このターン中に最大コンボを更新するたび、その増加分だけ回復する。戦技や奥義の発動に消費する。'

export type ComboSkillAvailabilityInput = {
  comboGauge: number
  energy?: number
  phase: BattlePhase
  animating: boolean
  busy: boolean
  hasPendingChoice: boolean
  handCount: number
  tacticalUsedThisTurn: boolean
  tacticalUsesThisTurn?: number
  tacticalUsesPerTurn?: number
  ultimateUsesThisTurn?: number
  ultimateUsesPerTurn?: number
  tacticalGaugeCost?: number
  ultimateGaugeCost?: number
  ultimateRequiresHand?: boolean
  tacticalEnergyCost?: number
  ultimateEnergyCost?: number
  skillsRestricted?: boolean
}

export type ComboSkillAvailability = {
  tacticalDisabled: boolean
  ultimateDisabled: boolean
}

export function getComboSkillAvailability({
  comboGauge,
  energy = Number.MAX_SAFE_INTEGER,
  phase,
  animating,
  busy,
  hasPendingChoice,
  handCount,
  tacticalUsedThisTurn,
  tacticalUsesThisTurn = tacticalUsedThisTurn ? 1 : 0,
  tacticalUsesPerTurn = tacticalUsedThisTurn ? 1 : 0,
  ultimateUsesThisTurn = 0,
  ultimateUsesPerTurn = 0,
  tacticalGaugeCost = TACTICAL_SKILL_COST,
  ultimateGaugeCost = ULTIMATE_SKILL_COST,
  ultimateRequiresHand = false,
  tacticalEnergyCost = 0,
  ultimateEnergyCost = 0,
  skillsRestricted = false,
}: ComboSkillAvailabilityInput): ComboSkillAvailability {
  const interactionLocked = animating
    || busy
    || phase !== 'PlayerInput'
    || hasPendingChoice
    || skillsRestricted

  return {
    tacticalDisabled: interactionLocked
      || (tacticalUsesPerTurn > 0 && tacticalUsesThisTurn >= tacticalUsesPerTurn)
      || comboGauge < tacticalGaugeCost
      || energy < tacticalEnergyCost,
    ultimateDisabled: interactionLocked
      || comboGauge < ultimateGaugeCost
      || energy < ultimateEnergyCost
      || (ultimateUsesPerTurn > 0 && ultimateUsesThisTurn >= ultimateUsesPerTurn)
      || (ultimateRequiresHand && handCount === 0),
  }
}

export function clampComboGauge(comboGauge: number): number {
  return Math.max(0, Math.min(COMBO_GAUGE_MAX, comboGauge))
}
