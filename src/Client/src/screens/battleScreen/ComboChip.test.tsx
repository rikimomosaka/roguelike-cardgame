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
  it('shows the current combo and a smaller turn maximum after the combo breaks', () => {
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

    const chip = screen.getByLabelText('現在0コンボ 最大4')
    expect(chip).toHaveTextContent('0(MAX4)COMBO')
    expect(chip).not.toHaveClass('is-active')

    fireEvent.mouseEnter(chip, { clientX: 20, clientY: 20 })
    const tooltip = screen.getByRole('tooltip')
    expect(within(tooltip).getByText('(このターンの最大コンボ:4)')).toBeInTheDocument()
    expect(within(tooltip).getByText('現在0コンボ。カードをプレイするとコンボ開始。')).toBeInTheDocument()
  })

  it('shows that the carried first card continues at full cost', () => {
    render(
      <TooltipHost>
        <ComboChip
          count={3}
          maxComboThisTurn={3}
          lastPlayedOrigCost={null}
          nextCardComboFreePass={false}
          comboCarryoverPending
        />
      </TooltipHost>,
    )

    const chip = screen.getByLabelText('現在3コンボ')
    fireEvent.mouseEnter(chip, { clientX: 20, clientY: 20 })
    expect(within(screen.getByRole('tooltip')).getByText(
      '現在3コンボ。連環により、次の1枚は任意コストでコンボ継続、軽減なし。',
    )).toBeInTheDocument()
  })
})
