import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { RunResultDto } from '../api/types'
import { RunResultScreen } from './RunResultScreen'
import { rarityToItemClass } from './runResultRarity'

const sample: RunResultDto = {
  schemaVersion: 1,
  accountId: 'acc',
  runId: 'run1',
  outcome: 'Cleared',
  actReached: 3,
  nodesVisited: 42,
  playSeconds: 3725,
  characterId: 'default',
  finalHp: 80,
  finalMaxHp: 100,
  finalGold: 500,
  finalDeck: [{ id: 'strike', upgraded: true }],
  finalRelics: ['coin_purse'],
  endedAtUtc: '2026-04-22T00:00:00Z',
  seenCardBaseIds: [],
  acquiredRelicIds: [],
  acquiredPotionIds: [],
  encounteredEnemyIds: [],
  journeyLog: [],
}

describe('RunResultScreen', () => {
  it.each([
    ['c', 'item--common'],
    ['r', 'item--rare'],
    ['e', 'item--epic'],
    ['l', 'item--legendary'],
  ] as const)('uses the canonical result class for %s rarity', (rarity, expected) => {
    expect(rarityToItemClass(rarity)).toBe(expected)
  })

  it('shows outcome, act reached, nodes, play seconds', () => {
    const { container } = render(<RunResultScreen result={sample} onReturnToMenu={vi.fn()} />)
    expect(screen.getByText(/Cleared/)).toBeDefined()
    expect(screen.getByText(/Act 3/)).toBeDefined()
    expect(screen.getByText(/42/)).toBeDefined()
    expect(screen.getByText(/01:02:05/)).toBeDefined()
    expect(container.querySelector('.rr')).toHaveAttribute('data-outcome', 'cleared')
    expect(container.querySelectorAll('.rr__reveal')).toHaveLength(4)
  })

  it('calls onReturnToMenu', () => {
    const cb = vi.fn()
    render(<RunResultScreen result={sample} onReturnToMenu={cb} />)
    fireEvent.click(screen.getByRole('button', { name: /メニュー/i }))
    expect(cb).toHaveBeenCalled()
  })
})
