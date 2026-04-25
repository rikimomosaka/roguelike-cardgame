import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { CardInstanceDto } from '../api/types'
import { TopBar } from './TopBar'

function baseProps(overrides: Partial<Parameters<typeof TopBar>[0]> = {}) {
  return {
    currentHp: 80,
    maxHp: 80,
    gold: 99,
    potions: ['', '', ''],
    deck: [
      { id: 'card_strike', upgraded: false },
      { id: 'card_defend', upgraded: false },
      { id: 'card_strike', upgraded: false },
    ] as CardInstanceDto[],
    relics: [] as string[],
    onDiscardPotion: vi.fn(),
    onOpenMenu: vi.fn(),
    ...overrides,
  }
}

describe('TopBar', () => {
  it('shows HP and Gold', () => {
    render(<TopBar {...baseProps()} />)
    expect(screen.getByText('HP 80/80')).toBeDefined()
    expect(
      screen.getAllByText((_, el) => /99\s*GOLD/.test(el?.textContent ?? '')).length,
    ).toBeGreaterThan(0)
  })

  it('renders the deck count on the deck button', () => {
    render(<TopBar {...baseProps()} />)
    expect(screen.getByLabelText('デッキ (3枚)')).toBeDefined()
  })

  it('toggles the deck list on click and lists every card', () => {
    render(<TopBar {...baseProps()} />)
    fireEvent.click(screen.getByLabelText('デッキ (3枚)'))
    const dialog = screen.getByRole('dialog', { name: '現在のデッキ' })
    const items = dialog.querySelectorAll('li')
    expect(items.length).toBe(3)
    fireEvent.click(screen.getByLabelText('デッキを閉じる'))
    expect(screen.queryByRole('dialog', { name: '現在のデッキ' })).toBeNull()
  })

  it('shows empty state when deck has no cards', () => {
    render(<TopBar {...baseProps({ deck: [] as CardInstanceDto[] })} />)
    fireEvent.click(screen.getByLabelText('デッキ (0枚)'))
    expect(screen.getByText('デッキは空です')).toBeDefined()
  })

  it('appends "+" to upgraded cards in the deck list', () => {
    render(
      <TopBar
        {...baseProps({
          deck: [
            { id: 'card_strike', upgraded: false },
            { id: 'card_strike', upgraded: true },
          ] as CardInstanceDto[],
        })}
      />,
    )
    fireEvent.click(screen.getByLabelText('デッキ (2枚)'))
    const dialog = screen.getByRole('dialog', { name: '現在のデッキ' })
    const items = Array.from(dialog.querySelectorAll('li')).map((li) => li.textContent ?? '')
    expect(items.some(t => /card_strike(?!\+)/.test(t))).toBe(true)
    expect(items.some(t => t.includes('card_strike+'))).toBe(true)
  })

  it('invokes onOpenMenu when the gear button is clicked', () => {
    const onOpenMenu = vi.fn()
    render(<TopBar {...baseProps({ onOpenMenu })} />)
    fireEvent.click(screen.getByLabelText('メニュー'))
    expect(onOpenMenu).toHaveBeenCalledTimes(1)
  })

  it('renders the list of held relics by name', () => {
    render(
      <TopBar {...baseProps({ relics: ['extra_max_hp', 'coin_purse'] })} />,
    )
    const list = screen.getByLabelText('レリック (2個)')
    const items = list.querySelectorAll('li')
    expect(items.length).toBe(2)
  })

  it('renders an empty relic list when no relics are held', () => {
    render(<TopBar {...baseProps()} />)
    const list = screen.getByLabelText('レリック (0個)')
    expect(list.querySelectorAll('li').length).toBe(0)
  })

  it('always renders the map-peek button; disabled without onTogglePeek', () => {
    const onTogglePeek = vi.fn()
    const { rerender } = render(<TopBar {...baseProps()} />)
    const btn = screen.getByLabelText('マップを見る') as HTMLButtonElement
    expect(btn.disabled).toBe(true)
    rerender(<TopBar {...baseProps({ onTogglePeek })} />)
    const enabled = screen.getByLabelText('マップを見る') as HTMLButtonElement
    expect(enabled.disabled).toBe(false)
    fireEvent.click(enabled)
    expect(onTogglePeek).toHaveBeenCalledTimes(1)
  })
})
