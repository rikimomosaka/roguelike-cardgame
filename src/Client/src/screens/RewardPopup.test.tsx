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
    isBossReward: false,
    rerollUsed: false,
    rerollAvailable: false,
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
    const proceed = screen.getByText('閉じる') as HTMLButtonElement
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
    expect((screen.getByText('閉じる') as HTMLButtonElement).disabled).toBe(false)
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
    const cardBtn = screen.getByText('カード報酬') as HTMLButtonElement
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
    fireEvent.click(screen.getByText('カード報酬'))
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
    expect(screen.queryByText((_, el) => /0\s*ゴールド/.test(el?.textContent ?? ''))).toBeNull()
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
    expect(screen.queryByText('閉じる')).toBeNull()
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
    expect(screen.getByText('閉じる')).toBeDefined()
    expect(screen.queryByText('次の層へ')).toBeNull()
  })

  it('card view shows only 閉じる button (no 戻る) when cardStatus is Pending', () => {
    const handlers = baseHandlers()
    render(
      <RewardPopup
        reward={baseReward({ goldClaimed: true })}
        potions={['', '', '']}
        potionSlotCount={3}
        {...handlers}
      />,
    )
    fireEvent.click(screen.getByText('カード報酬'))
    expect(screen.getByText('閉じる')).toBeDefined()
    expect(screen.queryByText('戻る')).toBeNull()
  })

  it('clicking 閉じる when Pending calls onSkipCard then closes card view', async () => {
    const handlers = baseHandlers()
    render(
      <RewardPopup
        reward={baseReward({ goldClaimed: true })}
        potions={['', '', '']}
        potionSlotCount={3}
        {...handlers}
      />,
    )
    fireEvent.click(screen.getByText('カード報酬'))
    fireEvent.click(screen.getByText('閉じる'))
    await waitFor(() => expect(handlers.onSkipCard).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(screen.queryByText('カードを選ぶ')).toBeNull())
  })

  it('card view shows 閉じる (not 戻る) when reopened after Skipped and does not re-call onSkipCard', async () => {
    // Regression: After Skip, reopening the card view must render "閉じる"
    // and clicking it must not hit the server again (server rejects SkipCard
    // when the reward is not Pending).
    const handlers = baseHandlers()
    render(
      <RewardPopup
        reward={baseReward({ goldClaimed: true, cardStatus: 'Skipped' })}
        potions={['', '', '']}
        potionSlotCount={3}
        {...handlers}
      />,
    )
    fireEvent.click(screen.getByText('カード報酬'))
    expect(screen.getByText('閉じる')).toBeDefined()
    expect(screen.queryByText('戻る')).toBeNull()
    fireEvent.click(screen.getByText('閉じる'))
    // onSkipCard は呼ばれてはならない（サーバ側 SkipCard は Pending 以外で 409）
    await waitFor(() => expect(screen.queryByText('カードを選ぶ')).toBeNull())
    expect(handlers.onSkipCard).not.toHaveBeenCalled()
  })

  // ── Reroll テスト (Phase 10.6.B T7) ─────────────────────────────────

  it('リロールボタンは rerollAvailable=true && !rerollUsed のとき card view に表示される', () => {
    const handlers = baseHandlers()
    const onRerollCard = vi.fn().mockResolvedValue(undefined)
    render(
      <RewardPopup
        reward={baseReward({ rerollAvailable: true, rerollUsed: false })}
        potions={['', '', '']}
        potionSlotCount={3}
        {...handlers}
        onRerollCard={onRerollCard}
      />,
    )
    fireEvent.click(screen.getByText('カード報酬'))
    expect(screen.getByText('リロール')).toBeDefined()
  })

  it('リロールボタンは rerollAvailable=false のとき表示されない', () => {
    const handlers = baseHandlers()
    const onRerollCard = vi.fn().mockResolvedValue(undefined)
    render(
      <RewardPopup
        reward={baseReward({ rerollAvailable: false, rerollUsed: false })}
        potions={['', '', '']}
        potionSlotCount={3}
        {...handlers}
        onRerollCard={onRerollCard}
      />,
    )
    fireEvent.click(screen.getByText('カード報酬'))
    expect(screen.queryByText('リロール')).toBeNull()
  })

  it('リロールボタンは rerollUsed=true のとき表示されない', () => {
    const handlers = baseHandlers()
    const onRerollCard = vi.fn().mockResolvedValue(undefined)
    render(
      <RewardPopup
        reward={baseReward({ rerollAvailable: true, rerollUsed: true })}
        potions={['', '', '']}
        potionSlotCount={3}
        {...handlers}
        onRerollCard={onRerollCard}
      />,
    )
    fireEvent.click(screen.getByText('カード報酬'))
    expect(screen.queryByText('リロール')).toBeNull()
  })

  it('リロールボタンをクリックすると onRerollCard が呼ばれる', async () => {
    const handlers = baseHandlers()
    const onRerollCard = vi.fn().mockResolvedValue(undefined)
    render(
      <RewardPopup
        reward={baseReward({ rerollAvailable: true, rerollUsed: false })}
        potions={['', '', '']}
        potionSlotCount={3}
        {...handlers}
        onRerollCard={onRerollCard}
      />,
    )
    fireEvent.click(screen.getByText('カード報酬'))
    fireEvent.click(screen.getByText('リロール'))
    await waitFor(() => expect(onRerollCard).toHaveBeenCalledTimes(1))
  })

  it('リロールボタンは cardStatus=Skipped のとき表示されない (resolved 扱い)', () => {
    const handlers = baseHandlers()
    const onRerollCard = vi.fn().mockResolvedValue(undefined)
    render(
      <RewardPopup
        reward={baseReward({
          rerollAvailable: true,
          rerollUsed: false,
          cardStatus: 'Skipped',
        })}
        potions={['', '', '']}
        potionSlotCount={3}
        {...handlers}
        onRerollCard={onRerollCard}
      />,
    )
    // Skipped 状態では cardView に入れないが、念のためボタン非表示確認
    expect(screen.queryByText('リロール')).toBeNull()
  })
})
