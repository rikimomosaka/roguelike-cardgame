import { fireEvent, render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { TooltipHost } from '../../components/Tooltip'
import { ComboChip } from '../BattleScreen'

vi.mock('../../hooks/useCardCatalog', () => ({
  useCardCatalog: () => ({ catalog: null, names: {} }),
}))

vi.mock('../../hooks/useUnitCatalog', () => ({
  useUnitCatalog: () => ({ catalog: null, names: {} }),
}))

describe('ComboChip', () => {
  it('keeps the turn maximum displayed and highlighted after the current combo breaks', () => {
    render(
      <TooltipHost>
        <ComboChip
          count={0}
          maxComboThisTurn={4}
          lastPlayedOrigCost={null}
          nextCardComboFreePass={false}
        />
      </TooltipHost>,
    )

    const chip = screen.getByLabelText('このターンの最大コンボ ×4')
    expect(chip).toHaveTextContent('COMBO×4')
    expect(chip).toHaveClass('is-active')

    fireEvent.mouseEnter(chip, { clientX: 20, clientY: 20 })
    const tooltip = screen.getByRole('tooltip')
    expect(within(tooltip).getByText('(このターンの最大コンボ:4)')).toBeInTheDocument()
    expect(within(tooltip).getByText('現在0コンボ。カードをプレイするとコンボ開始。')).toBeInTheDocument()
  })
})
