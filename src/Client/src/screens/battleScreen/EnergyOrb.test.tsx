import { fireEvent, render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { TooltipHost } from '../../components/Tooltip'
import { EnergyOrb } from '../BattleScreen'

vi.mock('../../hooks/useCardCatalog', () => ({
  useCardCatalog: () => ({ catalog: null, names: {} }),
}))

vi.mock('../../hooks/useUnitCatalog', () => ({
  useUnitCatalog: () => ({ catalog: null, names: {} }),
}))

describe('EnergyOrb', () => {
  it('shows one-digit current energy with the turn-start addition and carryover tooltip', () => {
    render(
      <TooltipHost>
        <EnergyOrb cur={3} recoveryPerTurn={3} />
      </TooltipHost>,
    )

    const orb = screen.getByLabelText('エナジー(現在3 ターン開始時に3追加)')
    expect(orb).toHaveTextContent('3/+3')

    fireEvent.focus(orb)
    const tooltip = screen.getByRole('tooltip')
    expect(within(tooltip).getByText('エナジー')).toBeInTheDocument()
    expect(within(tooltip).getByText('(現在3 ターン開始時に3追加)')).toBeInTheDocument()
    expect(within(tooltip).getByText(/次のターンへ持ち越され/)).toBeInTheDocument()
    expect(within(tooltip).queryByText(/最大値まで回復/)).not.toBeInTheDocument()
  })

  it('hides the recovery suffix for a two-digit current value', () => {
    render(<EnergyOrb cur={10} recoveryPerTurn={3} />)

    const orb = screen.getByLabelText('エナジー(現在10 ターン開始時に3追加)')
    expect(orb).toHaveTextContent('10')
    expect(within(orb).queryByText('/+3')).not.toBeInTheDocument()
  })

  it('caps a three-digit current value visually while preserving its exact amount for assistive text', () => {
    render(<EnergyOrb cur={100} recoveryPerTurn={3} />)

    const orb = screen.getByLabelText('エナジー(現在100 ターン開始時に3追加)')
    expect(orb).toHaveTextContent('99+')
    expect(within(orb).queryByText('/+3')).not.toBeInTheDocument()
  })
})
