export type EnergyOrbValue = {
  currentText: string
  recoveryText: string | null
  capped: boolean
}

/**
 * Keep the 64px orb readable while still exposing next-turn recovery for the
 * common one-digit state.
 */
export function formatEnergyOrbValue(
  current: number,
  recoveryPerTurn: number,
): EnergyOrbValue {
  if (current >= 100) {
    return { currentText: '99+', recoveryText: null, capped: true }
  }

  if (current >= 10) {
    return { currentText: `${current}`, recoveryText: null, capped: false }
  }

  return {
    currentText: `${current}`,
    recoveryText: `/+${recoveryPerTurn}`,
    capped: false,
  }
}

export function formatEnergyTooltipTitle(): string {
  return 'エナジー'
}

export function formatEnergyTooltipStatusLine(
  current: number,
  recoveryPerTurn: number,
): string {
  return `(現在${current} ターン開始時に${recoveryPerTurn}追加)`
}

export const ENERGY_CARRYOVER_DESCRIPTION =
  'カードをプレイするのに必要なリソース。ターン終了時に残っている分は次のターンへ持ち越され、ターン開始時に追加分を得る。'
