import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { RewardStateDto } from '../api/types'
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
    onProceed: vi.fn(),
    onDiscardPotion: vi.fn().mockResolvedValue(undefined),
  }
}

describe('RewardPopup', () => {
  it('進む is always enabled and invokes onProceed even with nothing claimed', () => {
    const handlers = baseHandlers()
    render(
      <RewardPopup
        reward={baseReward()}
        potions={['', '', '']}
        potionSlotCount={3}
        {...handlers}
      />,
    )
    const proceed = screen.getByText('進む') as HTMLButtonElement
    expect(proceed.disabled).toBe(false)
    fireEvent.click(proceed)
    expect(handlers.onProceed).toHaveBeenCalledTimes(1)
  })

  it('進む remains enabled when card reward is still Pending', () => {
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
    expect((screen.getByText('進む') as HTMLButtonElement).disabled).toBe(false)
  })

  it('allows reopening the card chooser after Skip to reclaim a card', async () => {
    const handlers = baseHandlers()
    const reward = baseReward({ goldClaimed: true, cardStatus: 'Skipped' })
    render(
      <RewardPopup
        reward={reward}
        potions={['', '', '']}
        potionSlotCount={3}
        {...handlers}
      />,
    )
    // Card button shows the reopen glyph and is still enabled (not Claimed).
    const cardBtn = screen.getByText('↩ カードの報酬') as HTMLButtonElement
    expect(cardBtn.disabled).toBe(false)
    fireEvent.click(cardBtn)
    fireEvent.click(screen.getByText('card_strike'))
    await waitFor(() =>
      expect(handlers.onPickCard).toHaveBeenCalledWith('card_strike'),
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
