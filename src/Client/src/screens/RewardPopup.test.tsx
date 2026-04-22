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
    relicId: null,
    relicClaimed: false,
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
    onClaimRelic: vi.fn().mockResolvedValue(undefined),
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
    // Card button is still enabled (not Claimed) and keeps the ✨ glyph.
    const cardBtn = screen.getByText('✨ カードの報酬') as HTMLButtonElement
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

  it('relic ボタンは relicId が設定されているとき表示される', () => {
    const handlers = baseHandlers()
    const reward = baseReward({ relicId: 'coin_purse', relicClaimed: false })
    render(
      <RewardPopup
        reward={reward}
        potions={['', '', '']}
        potionSlotCount={3}
        {...handlers}
      />,
    )
    // 名前解決はカタログ未ロードで raw id にフォールバックする
    expect(screen.getByText(/レリック: coin_purse/)).toBeDefined()
  })

  it('relic ボタンをクリックすると onClaimRelic が呼ばれる', async () => {
    const handlers = baseHandlers()
    const reward = baseReward({ relicId: 'coin_purse', relicClaimed: false })
    render(
      <RewardPopup
        reward={reward}
        potions={['', '', '']}
        potionSlotCount={3}
        {...handlers}
      />,
    )
    fireEvent.click(screen.getByText(/レリック: coin_purse/))
    await waitFor(() => expect(handlers.onClaimRelic).toHaveBeenCalledTimes(1))
  })

  it('relic は relicId が null のときは表示されない', () => {
    const handlers = baseHandlers()
    render(
      <RewardPopup
        reward={baseReward()}
        potions={['', '', '']}
        potionSlotCount={3}
        {...handlers}
      />,
    )
    expect(screen.queryByText(/レリック:/)).toBeNull()
  })

  it('relic は relicClaimed = true のときボタンが disabled', () => {
    const handlers = baseHandlers()
    const reward = baseReward({ relicId: 'coin_purse', relicClaimed: true })
    render(
      <RewardPopup
        reward={reward}
        potions={['', '', '']}
        potionSlotCount={3}
        {...handlers}
      />,
    )
    const btn = screen.getByText(/レリック: coin_purse/).closest('button') as HTMLButtonElement
    expect(btn.disabled).toBe(true)
  })

  it('gold が 0 のときは Gold 行が表示されない', () => {
    const handlers = baseHandlers()
    render(
      <RewardPopup
        reward={baseReward({ gold: 0 })}
        potions={['', '', '']}
        potionSlotCount={3}
        {...handlers}
      />,
    )
    expect(screen.queryByText(/0 Gold/)).toBeNull()
  })

  it('proceed button shows "次の層へ" when reward.isBossReward is true', () => {
    const handlers = baseHandlers()
    const reward = baseReward({ isBossReward: true })
    render(
      <RewardPopup
        reward={reward}
        potions={['', '', '']}
        potionSlotCount={3}
        {...handlers}
      />,
    )
    expect(screen.getByText('次の層へ')).toBeDefined()
    expect(screen.queryByText('進む')).toBeNull()
  })

  it('proceed button shows "進む" when reward.isBossReward is false', () => {
    const handlers = baseHandlers()
    const reward = baseReward({ isBossReward: false })
    render(
      <RewardPopup
        reward={reward}
        potions={['', '', '']}
        potionSlotCount={3}
        {...handlers}
      />,
    )
    expect(screen.getByText('進む')).toBeDefined()
    expect(screen.queryByText('次の層へ')).toBeNull()
  })
})
