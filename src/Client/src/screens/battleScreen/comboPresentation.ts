export type ComboDescriptionInput = {
  count: number
  lastPlayedOrigCost: number | null
  nextCardComboFreePass: boolean
  comboCarryoverPending?: boolean
}

/** Build the Combo chip tooltip from the authoritative battle state. */
export function formatComboDescription({
  count,
  lastPlayedOrigCost,
  nextCardComboFreePass,
  comboCarryoverPending = false,
}: ComboDescriptionInput): string {
  if (comboCarryoverPending) {
    return `現在${count}コンボ。連環により、次の1枚は任意コストでコンボ継続、軽減なし。`
  }

  if (count === 0) {
    return '現在0コンボ。カードをプレイするとコンボ開始。'
  }

  if (nextCardComboFreePass) {
    return `現在${count}コンボ。任意のカードをプレイする時、1コスト軽減してコンボ継続。`
  }

  const nextCost = lastPlayedOrigCost === null ? '?' : lastPlayedOrigCost + 1
  return `現在${count}コンボ。コスト${nextCost}のカードをプレイする時、1コスト軽減してコンボ継続。`
}

/** Build the separate turn-maximum line shown in the Combo tooltip. */
export function formatMaxComboThisTurn(maxComboThisTurn: number): string {
  return `(このターンの最大コンボ:${maxComboThisTurn})`
}
