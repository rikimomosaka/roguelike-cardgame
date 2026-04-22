import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { TopBar } from './TopBar'

function baseProps(overrides: Partial<Parameters<typeof TopBar>[0]> = {}) {
  return {
    currentHp: 80,
    maxHp: 80,
    gold: 99,
    potions: ['', '', ''],
    deck: ['card_strike', 'card_defend', 'card_strike'],
    onDiscardPotion: vi.fn(),
    onOpenMenu: vi.fn(),
    ...overrides,
  }
}

describe('TopBar', () => {
  it('shows HP and Gold', () => {
    render(<TopBar {...baseProps()} />)
    expect(screen.getByText('HP 80/80')).toBeDefined()
    expect(screen.getByText('Gold 99')).toBeDefined()
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
    render(<TopBar {...baseProps({ deck: [] })} />)
    fireEvent.click(screen.getByLabelText('デッキ (0枚)'))
    expect(screen.getByText('デッキは空です')).toBeDefined()
  })

  it('invokes onOpenMenu when the gear button is clicked', () => {
    const onOpenMenu = vi.fn()
    render(<TopBar {...baseProps({ onOpenMenu })} />)
    fireEvent.click(screen.getByLabelText('メニュー'))
    expect(onOpenMenu).toHaveBeenCalledTimes(1)
  })

  it('renders the map-peek button only when onTogglePeek is provided', () => {
    const onTogglePeek = vi.fn()
    const { rerender } = render(<TopBar {...baseProps()} />)
    expect(screen.queryByLabelText('マップを見る')).toBeNull()
    rerender(<TopBar {...baseProps({ onTogglePeek })} />)
    fireEvent.click(screen.getByLabelText('マップを見る'))
    expect(onTogglePeek).toHaveBeenCalledTimes(1)
  })
})
