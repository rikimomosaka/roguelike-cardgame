import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { MerchantInventoryDto } from '../api/types'
import { MerchantScreen } from './MerchantScreen'

vi.mock('../hooks/useCardCatalog', () => ({
  useCardCatalog: () => ({
    names: { strike: 'strike' },
    catalog: {
      strike: {
        id: 'strike',
        name: 'strike',
        displayName: 'strike',
        rarity: 1,
        cardType: 'attack',
        cost: 1,
        upgradedCost: 1,
        upgradable: true,
        description: '通常版テキスト。',
        upgradedDescription: '強化版テキスト。',
      },
    },
  }),
  usePotionCatalog: () => ({ names: {}, catalog: null }),
}))

const baseInventory: MerchantInventoryDto = {
  cards: [{ kind: 'card', id: 'strike', price: 50, sold: false }],
  relics: [{ kind: 'relic', id: 'coin_purse', price: 150, sold: false }],
  potions: [{ kind: 'potion', id: 'heal_potion_small', price: 50, sold: false }],
  discardSlotUsed: false,
  discardPrice: 75,
}

function baseHandlers() {
  return {
    onBuy: vi.fn().mockResolvedValue(undefined),
    onDiscard: vi.fn().mockResolvedValue(undefined),
    onLeave: vi.fn().mockResolvedValue(undefined),
  }
}

describe('MerchantScreen', () => {
  it('3 カテゴリと価格を表示する', () => {
    render(
      <MerchantScreen
        gold={500}
        deck={[{ id: 'strike', upgraded: false }]}
        inventory={baseInventory}
        {...baseHandlers()}
      />,
    )
    // カタログ未ロードなので raw id で表示される
    expect(screen.getAllByText('strike').length).toBeGreaterThanOrEqual(1)
    expect(screen.getByText('coin_purse')).toBeDefined()
    expect(screen.getByText('heal_potion_small')).toBeDefined()
    expect(screen.getAllByText((_, el) => /50\s*ゴールド/.test(el?.textContent ?? '')).length).toBeGreaterThanOrEqual(1)
  })

  it('gold 不足時もプレビュー操作を残しつつ購入だけ無効にする', () => {
    render(
      <MerchantScreen
        gold={30}
        deck={[]}
        inventory={baseInventory}
        {...baseHandlers()}
      />,
    )
    const btn = screen.getByRole('button', { name: /select strike/i }) as HTMLButtonElement
    expect(btn.disabled).toBe(false)
    expect(btn).toHaveAttribute('aria-pressed', 'false')
    const card = btn.querySelector('.card')!
    expect(card).toHaveTextContent('通常版テキスト。')
    fireEvent.mouseDown(card, { button: 2 })
    expect(card).toHaveTextContent('強化版テキスト。')
    fireEvent.mouseUp(window, { button: 2 })
    expect(card).toHaveTextContent('通常版テキスト。')
    fireEvent.click(btn)
    expect(screen.getByRole('status')).toHaveTextContent('こちらをお買い上げになりますか？')
    expect(screen.queryByRole('complementary')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /buy strike/i })).toBeDisabled()
  })

  it('onBuy は正しい引数で呼ばれる', async () => {
    const handlers = baseHandlers()
    render(
      <MerchantScreen
        gold={500}
        deck={[]}
        inventory={baseInventory}
        {...handlers}
      />,
    )
    expect(screen.getByRole('status')).toHaveTextContent('いらっしゃいませ。')
    fireEvent.click(screen.getByRole('button', { name: /select strike/i }))
    expect(screen.getByRole('status')).toHaveTextContent('こちらをお買い上げになりますか？')
    expect(screen.getByLabelText('価格 50ゴールド')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /buy strike/i }))
    await waitFor(() => expect(handlers.onBuy).toHaveBeenCalledWith('card', 'strike'))
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('ありがとうございます。'))
    expect(screen.queryByText('お買い上げありがとうございます。')).not.toBeInTheDocument()
    expect(screen.queryByText('取引が完了しました。')).not.toBeInTheDocument()
  })

  it('詳細トレイの購入処理中は二重送信を防ぐ', async () => {
    let finish!: () => void
    const handlers = baseHandlers()
    handlers.onBuy.mockImplementation(() => new Promise<void>(resolve => { finish = resolve }))
    render(<MerchantScreen gold={500} deck={[]} inventory={baseInventory} {...handlers} />)
    fireEvent.click(screen.getByRole('button', { name: /select strike/i }))
    const buy = screen.getByRole('button', { name: /buy strike/i })
    fireEvent.click(buy)
    fireEvent.click(buy)
    expect(handlers.onBuy).toHaveBeenCalledTimes(1)
    expect(buy).toBeDisabled()
    finish()
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('ありがとうございます。'))
    expect(screen.queryByText('取引が完了しました。')).not.toBeInTheDocument()
  })

  it('discardSlotUsed=true のとき除去ボタンは disabled', () => {
    const used = { ...baseInventory, discardSlotUsed: true }
    render(
      <MerchantScreen
        gold={500}
        deck={[{ id: 'strike', upgraded: false }]}
        inventory={used}
        {...baseHandlers()}
      />,
    )
    const btns = screen.queryAllByRole('button', { name: /discard/i })
    expect(btns.length).toBeGreaterThan(0)
    btns.forEach(b => expect((b as HTMLButtonElement).disabled).toBe(true))
  })

  it('除去ボタンを押すとデッキ一覧モーダルに遷移する', () => {
    render(
      <MerchantScreen
        gold={500}
        deck={[{ id: 'strike', upgraded: false }, { id: 'defend', upgraded: false }]}
        inventory={baseInventory}
        {...baseHandlers()}
      />,
    )
    // 初期状態では per-card の除去ボタンはない
    expect(screen.queryByRole('button', { name: /discard strike/i })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: /open discard view/i }))
    // モーダル内では deck 内の各カードに除去ボタンが表示される
    expect(screen.getByRole('button', { name: /discard strike at index 0/i })).toBeDefined()
    expect(screen.getByRole('button', { name: /discard defend at index 1/i })).toBeDefined()
    // 戻るボタンで shop モードへ
    fireEvent.click(screen.getByRole('button', { name: /^back$/i }))
    expect(screen.queryByRole('button', { name: /discard strike/i })).toBeNull()
  })

  it('除去モーダルで除去を選ぶと確認ダイアログが出て、はいで onDiscard が呼ばれる', async () => {
    const handlers = baseHandlers()
    render(
      <MerchantScreen
        gold={500}
        deck={[{ id: 'strike', upgraded: false }]}
        inventory={baseInventory}
        {...handlers}
      />,
    )
    fireEvent.click(screen.getByRole('button', { name: /open discard view/i }))
    fireEvent.click(screen.getByRole('button', { name: /discard strike at index 0/i }))
    // 確認ダイアログが表示される
    expect(screen.getByRole('dialog', { name: '削除しますか?' })).toBeDefined()
    // キャンセルすると onDiscard は呼ばれない
    fireEvent.click(screen.getByRole('button', { name: /cancel discard/i }))
    expect(handlers.onDiscard).not.toHaveBeenCalled()
    // 再度開いて確定
    fireEvent.click(screen.getByRole('button', { name: /discard strike at index 0/i }))
    fireEvent.click(screen.getByRole('button', { name: /confirm discard/i }))
    await waitFor(() => expect(handlers.onDiscard).toHaveBeenCalledWith(0))
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('ありがとうございます。'))
    expect(screen.queryByText('お買い上げありがとうございます。')).not.toBeInTheDocument()
  })

  it('カード除去に失敗した場合は確認ダイアログ内ですぐ通知する', async () => {
    const handlers = baseHandlers()
    handlers.onDiscard.mockRejectedValueOnce(new Error('discard failed'))
    render(
      <MerchantScreen
        gold={500}
        deck={[{ id: 'strike', upgraded: false }]}
        inventory={baseInventory}
        {...handlers}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: /open discard view/i }))
    fireEvent.click(screen.getByRole('button', { name: /discard strike at index 0/i }))
    fireEvent.click(screen.getByRole('button', { name: /confirm discard/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent('カードを削除できませんでした。')
    expect(screen.getByRole('dialog', { name: '削除しますか?' })).toBeInTheDocument()
  })

  it('立ち去るボタンで onLeave が呼ばれる', async () => {
    const handlers = baseHandlers()
    render(
      <MerchantScreen
        gold={500}
        deck={[]}
        inventory={baseInventory}
        {...handlers}
      />,
    )
    fireEvent.click(screen.getByRole('button', { name: /leave/i }))
    await waitFor(() => expect(handlers.onLeave).toHaveBeenCalledTimes(1))
  })
})
