import { fireEvent, render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { FinisherRecordDto } from '../../api/types'
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

const finisherRecord: FinisherRecordDto = {
  afterimageStacks: 1,
  isPlannedAfterimageIntentAvailable: true,
  plannedAfterimageIntent: null,
  cards: [{
    playSequence: 1,
    card: {
      cardInstanceId: 'saved-attack',
      definitionId: 'saved-attack',
      displayName: '保存攻撃',
      cardType: 'Attack',
      rarity: 'Common',
      isUpgraded: false,
      originalCost: 1,
      currentCost: 1,
      paidCost: 1,
      description: '敵単体に6アタック。',
      costChanges: [],
      poolClass: 'Striker',
    },
  }],
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
    expect(container.querySelector('.combo-gauge__value')).toHaveTextContent('7/10')
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
  it('shows a finisher record button below the gauge only while cards are saved', () => {
    const onOpenFinisherRecord = vi.fn()
    const { rerender } = render(
      <TooltipHost>
        <BattleComboControls
          {...baseProps}
          finisherRecord={finisherRecord}
          onOpenFinisherRecord={onOpenFinisherRecord}
        />
      </TooltipHost>,
    )

    const button = screen.getByRole('button', {
      name: '保存された残光を表示（1枚）',
    })
    expect(button).toHaveTextContent('保存された残光 1')
    fireEvent.click(button)
    expect(onOpenFinisherRecord).toHaveBeenCalledOnce()

    rerender(
      <TooltipHost>
        <BattleComboControls {...baseProps} finisherRecord={null} />
      </TooltipHost>,
    )
    expect(screen.queryByRole('button', { name: /保存された残光/ })).not.toBeInTheDocument()
  })

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
    expect(within(controls.children[2] as HTMLElement).getByRole('button')).toHaveTextContent('奥義消費 6')
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
      'ターン中1度だけ使用可能(0/1)。',
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
    const tooltip = screen.getByRole('tooltip')
    expect(tooltip).toHaveTextContent('ターン中1度だけ使用可能(0/1)。')
    expect(tooltip).toHaveTextContent('3枚引く。')
    expect(tooltip).toHaveTextContent('自身に節制3を付与。')
  })

  it('uses the configured skill name, usage limit, generated effect text, and status references', () => {
    render(
      <TooltipHost>
        <BattleComboControls
          {...baseProps}
          tacticalSkill={{
            name: '拡散の構え',
            energyCost: 0,
            comboGaugeCost: 3,
            oncePerTurn: true,
            description: '自身に[S:strength][N:1]を付与。自身に[S:omnistrike][N:1]を付与。',
          }}
        />
      </TooltipHost>,
    )

    const tactical = screen.getByRole('button', { name: '戦技：拡散の構え（コンボゲージ3消費）' })
    expect(tactical).toHaveTextContent('戦技消費 3')
    expect(tactical).not.toHaveTextContent('拡散の構え')
    expect(tactical.querySelector('.combo-skill-button__title')).toBeNull()
    expect(tactical.querySelector('.combo-skill-button__reason')).toBeNull()
    fireEvent.focus(tactical)
    const tooltip = screen.getByRole('tooltip')
    expect(tooltip).toHaveTextContent('戦技：拡散の構え')
    expect(tooltip).toHaveTextContent('(コンボゲージ3消費)')
    expect(tooltip).toHaveTextContent('ターン中1度だけ使用可能(0/1)。')
    expect(tooltip).toHaveTextContent('自身に')
    expect(tooltip).toHaveTextContent('筋力')
    expect(tooltip).toHaveTextContent('アタックで与えるダメージがX増加する。')
    expect(tooltip).toHaveTextContent('拡散')
  })

  it('replaces a legacy catalog usage header with exactly one live counter', () => {
    const { container } = render(
      <TooltipHost>
        <BattleComboControls
          {...baseProps}
          tacticalSkill={{
            name: '旧形式の戦技',
            comboGaugeCost: 3,
            energyCost: 0,
            oncePerTurn: true,
            usesPerTurn: 1,
            description: 'ターン中1度だけ使用可能(0/1)。\n自身に筋力1を付与。',
          }}
          tacticalUsesThisTurn={1}
        />
      </TooltipHost>,
    )

    fireEvent.focus(container.querySelector<HTMLElement>('[data-combo-skill="tactical"]')!)
    const tooltip = screen.getByRole('tooltip')
    expect(tooltip.textContent?.match(/ターン中1度だけ使用可能\(1\/1\)。/g)).toHaveLength(1)
    expect(tooltip).not.toHaveTextContent('ターン中1度だけ使用可能(0/1)。')
    expect(tooltip).toHaveTextContent('自身に筋力1を付与。')
  })

  it('keeps dual energy and gauge costs compact while explaining both in the tooltip', () => {
    render(
      <TooltipHost>
        <BattleComboControls
          {...baseProps}
          tacticalSkill={{
            name: '複合戦技',
            comboGaugeCost: 3,
            energyCost: 2,
            oncePerTurn: false,
            description: '敵単体に攻撃。',
          }}
        />
      </TooltipHost>,
    )

    const tactical = screen.getByRole('button', {
      name: '戦技：複合戦技（エナジー2 / コンボゲージ3消費）',
    })
    expect(tactical).toHaveTextContent('戦技消費 E2 / G3')
    expect(tactical).not.toHaveTextContent('複合戦技')
    fireEvent.focus(tactical)
    expect(screen.getByRole('tooltip')).toHaveTextContent('(エナジー2 / コンボゲージ3消費)')
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
    fireEvent.click(screen.getByRole('button', { name: '奥義（コンボゲージ6消費）' }))
    expect(onUseTactical).toHaveBeenCalledTimes(1)
    expect(onUseUltimate).not.toHaveBeenCalled()
  })

  it('renders absent participant skills as explicit disabled placeholders', () => {
    render(
      <TooltipHost>
        <BattleComboControls
          {...baseProps}
          tacticalUnconfigured
          ultimateUnconfigured
        />
      </TooltipHost>,
    )

    expect(screen.getByRole('button', { name: '戦技（未設定）' })).toBeDisabled()
    expect(screen.getByRole('button', { name: '奥義（未設定）' })).toBeDisabled()
    fireEvent.focus(screen.getByRole('button', { name: '戦技（未設定）' }).parentElement!)
    expect(screen.getByRole('tooltip')).toHaveTextContent('このキャラクターには設定されていません。')
  })
})
