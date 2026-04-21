import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { BattleStateDto } from '../api/types'
import { BattleOverlay } from './BattleOverlay'

function sampleBattle(): BattleStateDto {
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

  it('switches to peek view on マップを見る and returns on click', () => {
    render(<BattleOverlay battle={sampleBattle()} onWin={() => {}} />)
    fireEvent.click(screen.getByText('マップを見る'))
    const peek = screen.getByText('クリックで戦闘画面に戻る')
    expect(peek).toBeDefined()
    fireEvent.click(peek)
    expect(screen.getByText('勝利')).toBeDefined()
  })
})
