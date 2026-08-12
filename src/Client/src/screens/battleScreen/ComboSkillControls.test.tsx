import { fireEvent, render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { TooltipHost } from '../../components/Tooltip'
import { BattleComboControls, ComboGauge } from '../BattleScreen'

vi.mock('../../hooks/useCardCatalog', () => ({
  useCardCatalog: () => ({ catalog: null, names: {} }),
}))

vi.mock('../../hooks/useUnitCatalog', () => ({
  useUnitCatalog: () => ({ catalog: null, names: {} }),
}))

const baseProps = {
  count: 2,
  maxComboThisTurn: 4,
  lastPlayedOrigCost: 1,
  nextCardComboFreePass: false,
  comboGauge: 7,
  tacticalDisabled: false,
  ultimateDisabled: true,
  onUseTactical: vi.fn(),
  onUseUltimate: vi.fn(),
}

describe('ComboGauge', () => {
  it('exposes one meter and hides all ten visual segments from accessibility', () => {
    const { container } = render(<ComboGauge value={7} />)

    const meter = screen.getByRole('meter', { name: 'コンボゲージ 7/10' })
    expect(meter).toHaveAttribute('aria-valuenow', '7')
    const segments = container.querySelectorAll('.combo-gauge__segment')
    expect(segments).toHaveLength(10)
    expect(container.querySelectorAll('.combo-gauge__segment.is-filled')).toHaveLength(7)
    for (const segment of segments) expect(segment).toHaveAttribute('aria-hidden', 'true')
  })

  it('clamps the accessible and visual value to the gauge maximum', () => {
    const { container } = render(<ComboGauge value={15} />)
    expect(screen.getByRole('meter', { name: 'コンボゲージ 10/10' })).toBeInTheDocument()
    expect(container.querySelectorAll('.combo-gauge__segment.is-filled')).toHaveLength(10)
  })

  it('shows the combo gauge explanation and current value on a separate line', () => {
    render(
      <TooltipHost>
        <ComboGauge value={7} />
      </TooltipHost>,
    )
    fireEvent.focus(screen.getByRole('meter', { name: 'コンボゲージ 7/10' }))
    const tooltip = screen.getByRole('tooltip')
    expect(within(tooltip).getByText('コンボゲージ')).toBeInTheDocument()
    expect(within(tooltip).getByText('(現在7/10)')).toBeInTheDocument()
    expect(within(tooltip).getByText(/最大コンボを更新するたび/)).toBeInTheDocument()
  })
})

describe('BattleComboControls', () => {
  it('renders tactical, center combo column, and ultimate in that DOM order', () => {
    const { container } = render(
      <TooltipHost>
        <BattleComboControls {...baseProps} />
      </TooltipHost>,
    )

    const controls = container.querySelector('.battle__combo-controls')!
    expect(controls.children[0]).toHaveAttribute('data-combo-skill', 'tactical')
    expect(controls.children[1]).toHaveClass('battle__combo-column')
    expect(controls.children[2]).toHaveAttribute('data-combo-skill', 'ultimate')
    expect(within(controls.children[0] as HTMLElement).getByRole('button')).toHaveTextContent('戦技消費 3')
    expect(within(controls.children[2] as HTMLElement).getByRole('button')).toHaveTextContent('奥義消費 10')
    const center = controls.children[1]
    expect(center.children[0]).toHaveClass('battle__combo')
    expect(center.children[1]).toHaveClass('combo-gauge')
  })

  it('keeps a disabled tactical skill wrapper focusable and shows its exact tooltip', () => {
    const { container } = render(
      <TooltipHost>
        <BattleComboControls {...baseProps} tacticalDisabled />
      </TooltipHost>,
    )

    const wrapper = container.querySelector<HTMLElement>('[data-combo-skill="tactical"]')!
    const button = within(wrapper).getByRole('button')
    expect(button).toBeDisabled()
    expect(wrapper).toHaveAttribute('tabindex', '0')

    fireEvent.focus(wrapper)
    const tooltip = screen.getByRole('tooltip')
    expect(within(tooltip).getByText('戦技')).toBeInTheDocument()
    expect(within(tooltip).getByText('(コンボゲージ3消費)')).toBeInTheDocument()
    expect(within(tooltip).getByText(
      '1ターンに1度、自身の攻撃+1と拡散+1を得る。拡散がある間、攻撃はすべて全体攻撃になる。',
    )).toBeInTheDocument()
  })

  it('shows the exact ultimate tooltip while disabled for an empty hand', () => {
    const { container } = render(
      <TooltipHost>
        <BattleComboControls {...baseProps} ultimateDisabled />
      </TooltipHost>,
    )

    const wrapper = container.querySelector<HTMLElement>('[data-combo-skill="ultimate"]')!
    fireEvent.focus(wrapper)
    expect(screen.getByRole('tooltip')).toHaveTextContent(
      '発動時の手札それぞれに、その時点のコストと同量の軽減をこのターン終了まで与える。カードを使うと軽減は失われる。',
    )
  })

  it('invokes only enabled skill controls', () => {
    const onUseTactical = vi.fn()
    const onUseUltimate = vi.fn()
    render(
      <TooltipHost>
        <BattleComboControls
          {...baseProps}
          tacticalDisabled={false}
          ultimateDisabled
          onUseTactical={onUseTactical}
          onUseUltimate={onUseUltimate}
        />
      </TooltipHost>,
    )

    fireEvent.click(screen.getByRole('button', { name: '戦技（コンボゲージ3消費）' }))
    fireEvent.click(screen.getByRole('button', { name: '奥義（コンボゲージ10消費）' }))
    expect(onUseTactical).toHaveBeenCalledTimes(1)
    expect(onUseUltimate).not.toHaveBeenCalled()
  })
})
