import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { RelicCatalog } from '../api/catalog'
import type { RewardStateDto } from '../api/types'
import { TooltipHost } from '../components/Tooltip'
import { RewardPopup } from './RewardPopup'

const relicHookState = vi.hoisted(() => ({
  catalog: null as RelicCatalog | null,
  names: {} as Record<string, string>,
}))

vi.mock('../hooks/useRelicCatalog', () => ({
  useRelicCatalog: () => relicHookState,
}))

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
  beforeEach(() => {
    relicHookState.catalog = null
    relicHookState.names = {}
  })

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
    await waitFor(() => {
      expect(handlers.onPickCard).toHaveBeenCalledWith('card_strike')
      expect(screen.queryByText('カードを選ぶ')).toBeNull()
    })
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

  it('報酬受取中は二重送信と他の操作を防ぐ', async () => {
    let finish!: () => void
    const handlers = baseHandlers()
    handlers.onClaimGold.mockImplementation(() => new Promise<void>(resolve => { finish = resolve }))
    render(
      <RewardPopup reward={baseReward()} potions={[]} potionSlotCount={3} {...handlers} />,
    )
    const gold = screen.getByRole('button', { name: /15 ゴールド/ })
    fireEvent.click(gold)
    fireEvent.click(gold)
    expect(handlers.onClaimGold).toHaveBeenCalledTimes(1)
    expect(screen.getByText('報酬を受け取っています…')).toBeInTheDocument()
    expect(screen.getByText('閉じる')).toBeDisabled()
    finish()
    await waitFor(() => expect(screen.getByText('受け取りました')).toBeInTheDocument())
  })

  it('relic 行の hover で TopBar と共通の名称・レア度・効果説明を表示する', () => {
    relicHookState.catalog = {
      coin_purse: {
        id: 'coin_purse',
        name: 'コインの巾着',
        description: '効果とフレーバーを結合した互換テキスト',
        effectText: '入手時、20ゴールドを得る。',
        flavor: '旅人が落とした古い巾着。',
        rarity: 'Epic',
      },
    }
    relicHookState.names = { coin_purse: 'コインの巾着' }
    const handlers = baseHandlers()

    render(
      <TooltipHost>
        <RewardPopup
          reward={baseReward({ relicId: 'coin_purse', relicClaimed: false })}
          potions={['', '', '']}
          potionSlotCount={3}
          {...handlers}
        />
      </TooltipHost>,
    )

    const relicButton = screen.getByRole('button', { name: /レリック: コインの巾着/ })
    fireEvent.mouseEnter(relicButton, { clientX: 120, clientY: 100 })

    const tooltip = screen.getByRole('tooltip')
    expect(within(tooltip).getByText('コインの巾着')).toBeInTheDocument()
    expect(within(tooltip).getByText('EPIC')).toBeInTheDocument()
    expect(within(tooltip).getByText('入手時、20ゴールドを得る。')).toBeInTheDocument()
    expect(within(tooltip).getByText('旅人が落とした古い巾着。')).toBeInTheDocument()
    expect(within(tooltip).queryByText('効果とフレーバーを結合した互換テキスト')).toBeNull()

    fireEvent.mouseLeave(relicButton)
    expect(screen.queryByRole('tooltip')).toBeNull()
  })

  it('relic 行はキーボードフォーカスでも説明を表示して関連付ける', () => {
    relicHookState.catalog = {
      coin_purse: {
        id: 'coin_purse',
        name: 'コインの巾着',
        description: '入手時、20ゴールドを得る。',
        effectText: '入手時、20ゴールドを得る。',
        flavor: '',
        rarity: 'Common',
      },
    }
    relicHookState.names = { coin_purse: 'コインの巾着' }

    render(
      <TooltipHost>
        <RewardPopup
          reward={baseReward({ relicId: 'coin_purse', relicClaimed: false })}
          potions={['', '', '']}
          potionSlotCount={3}
          {...baseHandlers()}
        />
      </TooltipHost>,
    )

    const relicButton = screen.getByRole('button', { name: /レリック: コインの巾着/ })
    fireEvent.focus(relicButton)
    const tooltip = screen.getByRole('tooltip')
    expect(tooltip.id).not.toBe('')
    expect(relicButton).toHaveAttribute('aria-describedby', tooltip.id)
    expect(within(tooltip).getByText('入手時、20ゴールドを得る。')).toBeInTheDocument()

    fireEvent.blur(relicButton)
    expect(screen.queryByRole('tooltip')).toBeNull()
  })

  it('effectText が空の relic は flavor を説明欄へ重複表示しない', () => {
    relicHookState.catalog = {
      bell_earring: {
        id: 'bell_earring',
        name: 'べるイヤリング',
        description: '揺れるたびに澄んだ音色を響かせる。',
        effectText: '',
        flavor: '揺れるたびに澄んだ音色を響かせる。',
        rarity: 'Common',
      },
    }
    relicHookState.names = { bell_earring: 'べるイヤリング' }

    render(
      <TooltipHost>
        <RewardPopup
          reward={baseReward({ relicId: 'bell_earring', relicClaimed: false })}
          potions={['', '', '']}
          potionSlotCount={3}
          {...baseHandlers()}
        />
      </TooltipHost>,
    )

    const relicButton = screen.getByRole('button', { name: /レリック: べるイヤリング/ })
    fireEvent.mouseEnter(relicButton, { clientX: 120, clientY: 100 })
    const tooltip = screen.getByRole('tooltip')
    expect(within(tooltip).getAllByText('揺れるたびに澄んだ音色を響かせる。')).toHaveLength(1)
    expect(within(tooltip).getByText('—')).toBeInTheDocument()
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

  it('reward potion row shows the same tooltip surface as the top-bar potion', () => {
    const handlers = baseHandlers()
    const { container } = render(
      <TooltipHost>
        <RewardPopup
          reward={baseReward({ potionId: 'health_potion', potionClaimed: false })}
          potions={['', '', '']}
          potionSlotCount={3}
          {...handlers}
        />
      </TooltipHost>,
    )
    const row = container.querySelector('.rw-row--potion') as HTMLElement
    expect(row).not.toBeNull()
    fireEvent.mouseEnter(row, { clientX: 120, clientY: 100 })
    const tooltip = screen.getByRole('tooltip')
    expect(within(tooltip).getByText('health_potion')).toBeInTheDocument()
    expect(within(tooltip).getByText('ポーションの効果情報はありません。')).toBeInTheDocument()
    fireEvent.mouseLeave(row)
    expect(screen.queryByRole('tooltip')).toBeNull()
  })
})
