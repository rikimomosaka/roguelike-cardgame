import { render } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { ActorStats, formatActorStats } from './ActorStats'

describe('ActorStats', () => {
  it('shows every stat on its own row', () => {
    const { container } = render(
      <ActorStats stats={{ hp: 80, energyPerTurn: 3, drawPerTurn: 5, attack: 6, defense: 5 }} />,
    )
    const rows = Array.from(container.querySelectorAll('.actor-stats__item'))
      .map(row => row.textContent)
    expect(rows).toEqual(['HP80', '攻撃力6', '守備力5', 'ドロー5', 'エナジー3'])
  })

  it('does not disguise missing values as zero', () => {
    const stats = { hp: 80, energyPerTurn: undefined, drawPerTurn: undefined, attack: undefined, defense: 0 }
    const { container } = render(<ActorStats stats={stats} />)
    expect(container.querySelector('.actor-stats__item--attack')).toHaveTextContent('攻撃力—')
    expect(container.querySelector('.actor-stats__item--defense')).toHaveTextContent('守備力0')
    expect(formatActorStats(stats)).toBe('HP 80 / 攻撃力 — / 守備力 0 / ドロー — / エナジー —')
  })
})
