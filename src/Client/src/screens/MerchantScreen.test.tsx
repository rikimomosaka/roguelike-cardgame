import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { MerchantInventoryDto } from '../api/types'
import { MerchantScreen } from './MerchantScreen'

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

  it('gold 不足時は購入ボタンが disabled', () => {
    render(
      <MerchantScreen
        gold={30}
        deck={[]}
        inventory={baseInventory}
        {...baseHandlers()}
      />,
    )
    const btn = screen.getByRole('button', { name: /buy strike/i }) as HTMLButtonElement
    expect(btn.disabled).toBe(true)
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
    fireEvent.click(screen.getByRole('button', { name: /buy strike/i }))
    await waitFor(() => expect(handlers.onBuy).toHaveBeenCalledWith('card', 'strike'))
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

  it('除去モーダルで除去を選ぶと onDiscard が呼ばれ shop へ戻る', async () => {
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
    await waitFor(() => expect(handlers.onDiscard).toHaveBeenCalledWith(0))
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
