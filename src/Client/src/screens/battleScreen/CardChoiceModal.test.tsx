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

describe('CardChoiceModal Hand mode', () => {
  it('shows confirm button disabled until N selected', () => {
    render(
      <CardChoiceModal
        pending={basePending}
        hand={baseHand}
        cardNames={cardNames}
        cardTypeOf={typeOf}
        cardRarityOf={rarityOf}
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
        onConfirm={onConfirm}
      />,
    )
    fireEvent.click(screen.getByText('Strike'))
    fireEvent.click(screen.getByText('確定'))
    await waitFor(() => expect(onConfirm).toHaveBeenCalledWith(['c1']))
  })
})
