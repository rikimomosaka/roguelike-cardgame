import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { BattlePlaceholderStateDto } from '../api/types'
import { BattleOverlay } from './BattleOverlay'

function sampleBattle(): BattlePlaceholderStateDto {
  return {
    encounterId: 'enc_w_jaw_worm',
    outcome: 'Pending',
    enemies: [
      {
        enemyDefinitionId: 'jaw_worm',
        name: 'Jaw Worm',
        imageId: 'jaw_worm',
        currentHp: 42,
        maxHp: 42,
        currentMoveId: 'chomp',
      },
    ],
  }
}

describe('BattleOverlay', () => {
  it('renders enemy name, HP, and imageId', () => {
    render(<BattleOverlay battle={sampleBattle()} onWin={() => {}} />)
    expect(screen.getByText('Jaw Worm')).toBeDefined()
    expect(screen.getByText('HP 42/42')).toBeDefined()
    expect(screen.getByText('jaw_worm')).toBeDefined()
  })

  it('invokes onWin when 勝利 button is clicked', async () => {
    const onWin = vi.fn().mockResolvedValue(undefined)
    render(<BattleOverlay battle={sampleBattle()} onWin={onWin} />)
    fireEvent.click(screen.getByText('勝利'))
    expect(onWin).toHaveBeenCalledTimes(1)
  })

  it('does not render a map-peek button — that control lives in TopBar', () => {
    render(<BattleOverlay battle={sampleBattle()} onWin={() => {}} />)
    expect(screen.queryByText('マップを見る')).toBeNull()
  })

  it('shows DEBUG -10HP button and fires onDebugDamage', () => {
    const onDebugDamage = vi.fn()
    render(<BattleOverlay battle={sampleBattle()} onWin={() => {}} onDebugDamage={onDebugDamage} />)
    const btn = screen.getByRole('button', { name: /DEBUG -10HP/ })
    fireEvent.click(btn)
    expect(onDebugDamage).toHaveBeenCalled()
  })
})
