import type { BattleCardInstanceDto, BattleEventStepDto } from '../../api/types'

/** ドロー演出中に表示する手札・山札・捨札。 */
export type DrawPileState = {
  hand: BattleCardInstanceDto[]
  drawPile: BattleCardInstanceDto[]
  discardPile: BattleCardInstanceDto[]
}

export type InlineDrawAction =
  /** cards を 1 枚ずつ山札から手札へ飛ばす。states[i] は i 枚目が着地した後の表示。 */
  | { kind: 'draw'; cards: BattleCardInstanceDto[]; states: DrawPileState[] }
  /** 捨札 cards を山札へ戻す演出。stateAfter はその後の表示。 */
  | { kind: 'reshuffle'; cards: BattleCardInstanceDto[]; stateAfter: DrawPileState }

export type InlineDrawPlan = {
  /** ステップ index → そのイベントの位置で流す演出。 */
  actionsByStepIndex: Map<number, InlineDrawAction[]>
  /** イベントから割り当てきれなかった残りのドロー（安全側）。 */
  trailing: InlineDrawAction[]
}

function belongsTo(step: BattleEventStepDto, actorInstanceId: string | undefined): boolean {
  if (!actorInstanceId) return true
  const target = step.event.targetInstanceId
  return target === null || target === undefined || target === actorInstanceId
}

/**
 * カード効果の途中で起きたドロー・山札再構築を、そのイベントの位置で演出するための計画。
 * Core は 1 回のドロー効果につき「Shuffle（必要時）→ Draw(合計枚数)」の順でイベントを出し、
 * 引いたカードは手札の末尾へ引いた順に並ぶ。Shuffle の位置で再構築前に引いた分と
 * 再構築演出を、Draw の位置で残りを流す。
 */
export function planInlineDraws(input: {
  steps: readonly BattleEventStepDto[]
  actorInstanceId: string | undefined
  /** 引いたカード（引いた順）。 */
  newlyDrawn: readonly BattleCardInstanceDto[]
  /** ドロー前から手札に残っているカード。 */
  survivingHand: readonly BattleCardInstanceDto[]
  /** 行動前の山札・捨札。 */
  preDrawPile: readonly BattleCardInstanceDto[]
  preDiscardPile: readonly BattleCardInstanceDto[]
  /** 山札再構築で捨札から戻るカード一式（再構築が無ければ null）。 */
  reshuffleCards: readonly BattleCardInstanceDto[] | null
}): InlineDrawPlan {
  const preDrawIds = new Set(input.preDrawPile.map(card => card.instanceId))
  const actionsByStepIndex = new Map<number, InlineDrawAction[]>()
  let hand = [...input.survivingHand]
  let drawPile = [...input.preDrawPile]
  let discardPile = [...(input.reshuffleCards ?? input.preDiscardPile)]
  let cursor = 0
  let credit = 0
  let reshuffled = false

  const drawCards = (cards: readonly BattleCardInstanceDto[]): InlineDrawAction | null => {
    if (cards.length === 0) return null
    const states: DrawPileState[] = []
    for (const card of cards) {
      hand = [...hand, card]
      const index = drawPile.findIndex(candidate => candidate.instanceId === card.instanceId)
      drawPile = index >= 0
        ? [...drawPile.slice(0, index), ...drawPile.slice(index + 1)]
        : drawPile.slice(1)
      states.push({ hand, drawPile, discardPile })
    }
    return { kind: 'draw', cards: [...cards], states }
  }
  const push = (stepIndex: number, action: InlineDrawAction | null) => {
    if (!action) return
    const list = actionsByStepIndex.get(stepIndex) ?? []
    list.push(action)
    actionsByStepIndex.set(stepIndex, list)
  }

  input.steps.forEach((step, stepIndex) => {
    if (!belongsTo(step, input.actorInstanceId)) return
    const amount = step.event.amount ?? 0
    if (step.event.kind === 'Shuffle' && amount > 0
        && input.reshuffleCards && !reshuffled) {
      // 再構築前に旧山札から引いた分を先に飛ばす（この分は直後の Draw に含まれる）。
      const before: BattleCardInstanceDto[] = []
      while (cursor < input.newlyDrawn.length
        && preDrawIds.has(input.newlyDrawn[cursor].instanceId)) {
        before.push(input.newlyDrawn[cursor])
        cursor += 1
      }
      credit += before.length
      push(stepIndex, drawCards(before))
      drawPile = [...input.reshuffleCards]
      discardPile = []
      reshuffled = true
      push(stepIndex, {
        kind: 'reshuffle',
        cards: [...input.reshuffleCards],
        stateAfter: { hand, drawPile, discardPile },
      })
      return
    }
    if (step.event.kind === 'Draw' && amount > 0) {
      const count = Math.max(0, amount - credit)
      credit = 0
      const cards = input.newlyDrawn.slice(cursor, cursor + count)
      cursor += cards.length
      push(stepIndex, drawCards(cards))
    }
  })

  const trailing: InlineDrawAction[] = []
  const rest = drawCards(input.newlyDrawn.slice(cursor))
  if (rest) trailing.push(rest)
  return { actionsByStepIndex, trailing }
}

/** そのアクションに、表示中の参加者のドロー／山札再構築イベントが含まれるか。 */
export function hasInlineDrawEvent(
  steps: readonly BattleEventStepDto[],
  actorInstanceId: string | undefined,
): boolean {
  return steps.some(step => (step.event.kind === 'Draw' || step.event.kind === 'Shuffle')
    && (step.event.amount ?? 0) > 0
    && belongsTo(step, actorInstanceId))
}
