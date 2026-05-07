import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import { CardChoiceModal } from './CardChoiceModal'
import type {
  BattleCardInstanceDto,
  PendingCardPlayDto,
} from '../../api/types'

const basePending: PendingCardPlayDto = {
  cardInstanceId: 'play_card',
  effectIndex: 0,
  choice: {
    action: 'exhaustCard',
    pile: 'hand',
    count: 1,
    candidateInstanceIds: ['c1', 'c2'],
  },
}

const baseHand: BattleCardInstanceDto[] = [
  {
    instanceId: 'c1',
    cardDefinitionId: 'strike',
    isUpgraded: false,
    costOverride: null,
    adjustedDescription: null,
    adjustedUpgradedDescription: null,
  },
  {
    instanceId: 'c2',
    cardDefinitionId: 'defend',
    isUpgraded: false,
    costOverride: null,
    adjustedDescription: null,
    adjustedUpgradedDescription: null,
  },
  {
    instanceId: 'play_card',
    cardDefinitionId: 'choose_card',
    isUpgraded: false,
    costOverride: null,
    adjustedDescription: null,
    adjustedUpgradedDescription: null,
  },
]

const cardNames = {
  strike: 'Strike',
  defend: 'Defend',
  choose_card: 'Choose',
}
const typeOf = () => 'skill' as const
const rarityOf = () => 'c' as const
const costOf = () => 1

describe('CardChoiceModal Hand mode', () => {
  it('shows confirm button disabled until N selected', () => {
    render(
      <CardChoiceModal
        pending={basePending}
        hand={baseHand}
        cardNames={cardNames}
        cardTypeOf={typeOf}
        cardRarityOf={rarityOf}
        cardCostOf={costOf}
        onConfirm={vi.fn()}
      />,
    )
    const btn = screen.getByText('確定') as HTMLButtonElement
    expect(btn.disabled).toBe(true)
  })

  it('selecting candidate enables confirm', () => {
    render(
      <CardChoiceModal
        pending={basePending}
        hand={baseHand}
        cardNames={cardNames}
        cardTypeOf={typeOf}
        cardRarityOf={rarityOf}
        cardCostOf={costOf}
        onConfirm={vi.fn()}
      />,
    )
    fireEvent.click(screen.getByText('Strike'))
    const btn = screen.getByText('確定') as HTMLButtonElement
    expect(btn.disabled).toBe(false)
  })

  it('confirm calls onConfirm with selected ids', async () => {
    const onConfirm = vi.fn().mockResolvedValue(undefined)
    render(
      <CardChoiceModal
        pending={basePending}
        hand={baseHand}
        cardNames={cardNames}
        cardTypeOf={typeOf}
        cardRarityOf={rarityOf}
        cardCostOf={costOf}
        onConfirm={onConfirm}
      />,
    )
    fireEvent.click(screen.getByText('Strike'))
    fireEvent.click(screen.getByText('確定'))
    await waitFor(() => expect(onConfirm).toHaveBeenCalledWith(['c1']))
  })

  // Phase 10.5.M2-Choose T7 follow-up (Minor #10):
  //  非候補カード (= candidateInstanceIds に含まれない) のクリックは
  //  選択 state を変えず、結果として「確定」ボタンは disabled のまま。
  //  fix #4 で played card は表示から除外されるため、別途非候補カードを
  //  hand に注入してテストする。
  it('clicking non-candidate does nothing', () => {
    const handWithExtra: BattleCardInstanceDto[] = [
      ...baseHand,
      {
        instanceId: 'extra',
        cardDefinitionId: 'fire',
        isUpgraded: false,
        costOverride: null,
        adjustedDescription: null,
        adjustedUpgradedDescription: null,
      },
    ]
    const onConfirm = vi.fn().mockResolvedValue(undefined)
    render(
      <CardChoiceModal
        pending={basePending}
        hand={handWithExtra}
        cardNames={{ ...cardNames, fire: 'Fire' }}
        cardTypeOf={typeOf}
        cardRarityOf={rarityOf}
        cardCostOf={costOf}
        onConfirm={onConfirm}
      />,
    )
    // 'Fire' は候補ではない (basePending.candidateInstanceIds は ['c1','c2'])
    fireEvent.click(screen.getByText('Fire'))
    const btn = screen.getByText('確定') as HTMLButtonElement
    expect(btn.disabled).toBe(true)
  })
})

// Phase 10.5.M2-Choose T8: pile=draw / discard 用 List モード。
//  カード名のみの縦リスト。upgraded は名前末尾に "+" を付ける。
const drawPending: PendingCardPlayDto = {
  cardInstanceId: 'play_card',
  effectIndex: 0,
  choice: {
    action: 'exhaustCard',
    pile: 'draw',
    count: 1,
    candidateInstanceIds: ['d1', 'd2'],
  },
}
const drawPile: BattleCardInstanceDto[] = [
  {
    instanceId: 'd1',
    cardDefinitionId: 'fire',
    isUpgraded: false,
    costOverride: null,
    adjustedDescription: null,
    adjustedUpgradedDescription: null,
  },
  {
    instanceId: 'd2',
    cardDefinitionId: 'ice',
    isUpgraded: true,
    costOverride: null,
    adjustedDescription: null,
    adjustedUpgradedDescription: null,
  },
]

describe('CardChoiceModal List mode', () => {
  it('shows draw pile cards as list rows', () => {
    render(
      <CardChoiceModal
        pending={drawPending}
        hand={[]}
        drawPile={drawPile}
        cardNames={{ fire: '炎', ice: '氷' }}
        cardTypeOf={typeOf}
        cardRarityOf={rarityOf}
        cardCostOf={costOf}
        onConfirm={vi.fn()}
      />,
    )
    expect(screen.getByText('炎')).toBeTruthy()
    // upgraded → 名前末尾に "+"
    expect(screen.getByText('氷+')).toBeTruthy()
  })

  it('clicking row selects, confirm sends id', async () => {
    const onConfirm = vi.fn().mockResolvedValue(undefined)
    render(
      <CardChoiceModal
        pending={drawPending}
        hand={[]}
        drawPile={drawPile}
        cardNames={{ fire: '炎', ice: '氷' }}
        cardTypeOf={typeOf}
        cardRarityOf={rarityOf}
        cardCostOf={costOf}
        onConfirm={onConfirm}
      />,
    )
    fireEvent.click(screen.getByText('炎'))
    fireEvent.click(screen.getByText('確定'))
    await waitFor(() => expect(onConfirm).toHaveBeenCalledWith(['d1']))
  })
})
