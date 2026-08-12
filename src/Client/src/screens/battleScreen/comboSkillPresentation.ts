import type { BattlePhase } from '../../api/types'

export const COMBO_GAUGE_MAX = 10
export const TACTICAL_SKILL_COST = 3
export const ULTIMATE_SKILL_COST = 10

export const TACTICAL_SKILL_DESCRIPTION =
  '1ターンに1度、自身の攻撃+1と拡散+1を得る。拡散がある間、攻撃はすべて全体攻撃になる。'
export const ULTIMATE_SKILL_DESCRIPTION =
  '発動時の手札それぞれに、その時点のコストと同量の軽減をこのターン終了まで与える。カードを使うと軽減は失われる。'
export const COMBO_GAUGE_DESCRIPTION =
  'このターン中に最大コンボを更新するたび、その増加分だけ回復する。戦技や奥義の発動に消費する。'

export type ComboSkillAvailabilityInput = {
  comboGauge: number
  phase: BattlePhase
  animating: boolean
  busy: boolean
  hasPendingChoice: boolean
  handCount: number
  tacticalUsedThisTurn: boolean
}

export type ComboSkillAvailability = {
  tacticalDisabled: boolean
  ultimateDisabled: boolean
}

export function getComboSkillAvailability({
  comboGauge,
  phase,
  animating,
  busy,
  hasPendingChoice,
  handCount,
  tacticalUsedThisTurn,
}: ComboSkillAvailabilityInput): ComboSkillAvailability {
  const interactionLocked = animating
    || busy
    || phase !== 'PlayerInput'
    || hasPendingChoice

  return {
    tacticalDisabled: interactionLocked
      || tacticalUsedThisTurn
      || comboGauge < TACTICAL_SKILL_COST,
    ultimateDisabled: interactionLocked
      || comboGauge < ULTIMATE_SKILL_COST
      || handCount === 0,
  }
}

export function clampComboGauge(comboGauge: number): number {
  return Math.max(0, Math.min(COMBO_GAUGE_MAX, comboGauge))
}
