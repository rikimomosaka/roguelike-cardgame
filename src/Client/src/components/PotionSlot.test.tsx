import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { PotionSlot } from './PotionSlot'

vi.mock('../hooks/useCardCatalog', () => ({
  usePotionCatalog: () => ({
    names: { health_potion: 'Health Potion', fire_potion: 'Fire Potion' },
    catalog: {
      health_potion: {
        id: 'health_potion',
        name: 'Health Potion',
        rarity: 1,
        usableOutsideBattle: true,
        description: 'Heal 15.',
      },
      fire_potion: {
        id: 'fire_potion',
        name: 'Fire Potion',
        rarity: 1,
        usableOutsideBattle: false,
        description: 'Deal 20 damage.',
      },
      limited_potion: {
        id: 'limited_potion',
        name: 'Limited Potion',
        rarity: 1,
        usableOutsideBattle: false,
        usesPerTurn: 1,
        description: 'ターン中1度だけ使用可能(0/1)。\nHeal 1.',
      },
    },
  }),
}))

describe('PotionSlot outside-battle use', () => {
  it('offers use for a potion marked usable outside battle', () => {
    const onUse = vi.fn()
    render(
      <PotionSlot
        slotIndex={0}
        potionId="health_potion"
        onDiscard={vi.fn()}
        onUse={onUse}
        outsideBattleUseOnly
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: /health_potion/ }))
    const menu = screen.getByRole('menu')
    const buttons = menu.querySelectorAll('button')
    expect(buttons.length).toBe(3)
    fireEvent.click(buttons[0])
    expect(onUse).toHaveBeenCalledTimes(1)
  })

  it('does not offer use for a battle-only potion outside battle', () => {
    render(
      <PotionSlot
        slotIndex={0}
        potionId="fire_potion"
        onDiscard={vi.fn()}
        onUse={vi.fn()}
        outsideBattleUseOnly
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: /fire_potion/ }))
    expect(screen.getByRole('menu').querySelectorAll('button').length).toBe(2)
  })

  it('does not offer use after the potion-level turn limit is reached', () => {
    render(
      <PotionSlot
        slotIndex={0}
        potionId="limited_potion"
        onDiscard={vi.fn()}
        onUse={vi.fn()}
        usesThisTurn={1}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: /limited_potion/ }))
    expect(screen.getByRole('menu').querySelectorAll('button').length).toBe(2)
  })
})
