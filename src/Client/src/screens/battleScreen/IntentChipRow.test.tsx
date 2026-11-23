import { render } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { TooltipHost } from '../../components/Tooltip'
import { IntentChipRow, type IntentDemo } from '../BattleScreen'

vi.mock('../../hooks/useCardCatalog', () => ({
  useCardCatalog: () => ({ catalog: null, names: {} }),
}))

vi.mock('../../hooks/useUnitCatalog', () => ({
  useUnitCatalog: () => ({ catalog: null, names: {} }),
}))

describe('IntentChipRow', () => {
  it('renders every attack scope as its own row with its own icon', () => {
    const attack: IntentDemo = {
      kind: 'attack',
      icon: '⚔',
      name: '攻撃',
      desc: '複数範囲への攻撃。',
      attack: {
        single: 33,
        singleHits: 2,
        random: 12,
        randomHits: 3,
        all: 7,
        allHits: 1,
      },
    }

    const { container } = render(
      <TooltipHost>
        <IntentChipRow intents={[attack]} />
      </TooltipHost>,
    )

    const rows = [...container.querySelectorAll('.intent__scope-row')]
    expect(rows).toHaveLength(3)
    expect(rows.map(row => row.querySelector('.intent__scope-label')?.textContent))
      .toEqual(['SINGLE', 'RANDOM', 'ALL'])
    expect(rows.map(row => row.querySelector('.intent__scope-summary')?.getAttribute('data-attack-summary')))
      .toEqual(['33(2HIT)', '12(3HIT)', '7(1HIT)'])
    expect(rows.map(row => row.querySelector('img')?.getAttribute('src')))
      .toEqual([
        '/icons/ui/attack.png',
        '/icons/ui/attack_random.png',
        '/icons/ui/attack_all.png',
      ])
    for (const row of rows) {
      expect(row.querySelectorAll('.intent__icon')).toHaveLength(1)
      expect(row.querySelectorAll('.intent__scope-summary')).toHaveLength(1)
    }
    expect(container.querySelector('.intent__scope-separator')).toBeNull()
  })
})
