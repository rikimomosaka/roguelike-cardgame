import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { RewardStateDto } from '../api/types'
import { ApiError } from '../api/client'
import { RewardPopup } from './RewardPopup'

function baseReward(overrides: Partial<RewardStateDto> = {}): RewardStateDto {
  return {
    gold: 15,
    goldClaimed: false,
    potionId: null,
    potionClaimed: false,
    cardChoices: ['card_strike', 'card_bash', 'card_defend'],
    cardStatus: 'Pending',
    ...overrides,
  }
}

function baseHandlers() {
  return {
    onClaimGold: vi.fn().mockResolvedValue(undefined),
    onClaimPotion: vi.fn().mockResolvedValue(undefined),
    onPickCard: vi.fn().mockResolvedValue(undefined),
    onSkipCard: vi.fn().mockResolvedValue(undefined),
    onProceed: vi.fn().mockResolvedValue(undefined),
    onDiscardPotion: vi.fn().mockResolvedValue(undefined),
    onPotionFullAlert: vi.fn(),
  }
}

describe('RewardPopup', () => {
  it('enables 進む only when all rewards resolved, and invokes onProceed', async () => {
    const handlers = baseHandlers()
    const reward = baseReward({
      goldClaimed: true,
      cardStatus: 'Claimed',
    })
    render(
      <RewardPopup
        reward={reward}
        potions={['', '', '']}
        potionSlotCount={3}
        {...handlers}
      />,
    )
    const proceed = screen.getByText('進む') as HTMLButtonElement
    expect(proceed.disabled).toBe(false)
    fireEvent.click(proceed)
    await waitFor(() => expect(handlers.onProceed).toHaveBeenCalledTimes(1))
  })

  it('disables 進む while card reward is Pending', () => {
    const handlers = baseHandlers()
    const reward = baseReward({ goldClaimed: true, cardStatus: 'Pending' })
    render(
      <RewardPopup
        reward={reward}
        potions={['', '', '']}
        potionSlotCount={3}
        {...handlers}
      />,
    )
    expect((screen.getByText('進む') as HTMLButtonElement).disabled).toBe(true)
  })

  it('invokes onPotionFullAlert when claim potion throws 409', async () => {
    const handlers = baseHandlers()
    handlers.onClaimPotion.mockRejectedValue(new ApiError(409, 'potions full'))
    const reward = baseReward({ potionId: 'potion_heal', cardStatus: 'Claimed' })
    render(
      <RewardPopup
        reward={reward}
        potions={['p1', 'p2', 'p3']}
        potionSlotCount={3}
        {...handlers}
      />,
    )
    fireEvent.click(screen.getByText('🧪 potion_heal'))
    await waitFor(() =>
      expect(handlers.onPotionFullAlert).toHaveBeenCalledTimes(1),
    )
  })

  it('switches to card view and returns after picking a card', async () => {
    const handlers = baseHandlers()
    const reward = baseReward({ goldClaimed: true })
    render(
      <RewardPopup
        reward={reward}
        potions={['', '', '']}
        potionSlotCount={3}
        {...handlers}
      />,
    )
    fireEvent.click(screen.getByText('✨ カードの報酬'))
    expect(screen.getByText('カードを選ぶ')).toBeDefined()
    fireEvent.click(screen.getByText('card_strike'))
    await waitFor(() =>
      expect(handlers.onPickCard).toHaveBeenCalledWith('card_strike'),
    )
    expect(screen.queryByText('カードを選ぶ')).toBeNull()
  })
})
