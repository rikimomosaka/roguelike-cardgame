import { describe, expect, it, vi } from 'vitest'
import { render, screen, fireEvent, within } from '@testing-library/react'
import { EffectEditor } from './EffectEditor'
import { emptyEffect } from './DevSpecTypes'
import type { DevMeta } from '../../api/dev'

const meta: DevMeta = {
  cardTypes: [],
  cardAbilities: ['finisher', 'reprise', 'contingency'],
  contingencyTriggerTimings: ['TurnStart', 'BeforeEnemyAttack', 'ComboReached'],
  rarities: [],
  effectActions: [
    'attack', 'draw', 'exhaustSelf', 'gainComboGauge', 'modifyCardCost',
    'recoverFromDiscard', 'gainMaxHp', 'gainGold', 'healHp',
  ],
  effectScopes: ['Self', 'Single', 'All'],
  effectSides: ['Enemy', 'Ally'],
  piles: ['hand', 'draw', 'discard', 'exhaust'],
  selectModes: ['random', 'choose', 'all'],
  triggers: ['OnTurnStart', 'OnHit'],
  amountSources: ['handCount', 'maxComboThisTurn', 'currentCost'],
  keywords: [],
  statuses: [{ id: 'weak', jp: '脱力', kind: 'Debuff' }],
}

describe('EffectEditor', () => {
  it('shows scope/side/amount fields for action=attack', () => {
    render(
      <EffectEditor
        effect={emptyEffect()}
        meta={meta}
        allCardIds={[]}
        onChange={() => {}}
        onRemove={() => {}}
      />,
    )
    // scope, side, amount, amountSource, trigger が表示される (Amount は label)
    expect(screen.getByLabelText(/^effect amount$/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/effect action/i)).toBeInTheDocument()
    expect(screen.queryByLabelText(/effect uses per turn/i)).not.toBeInTheDocument()
  })

  it('hides scope/side/amount for action=exhaustSelf (no fields)', () => {
    const onChange = vi.fn()
    const { rerender } = render(
      <EffectEditor
        effect={{ ...emptyEffect(), action: 'attack' }}
        meta={meta}
        allCardIds={[]}
        onChange={onChange}
        onRemove={() => {}}
      />,
    )
    expect(screen.queryByLabelText(/^effect amount$/i)).toBeInTheDocument()

    // exhaustSelf に切り替えると amount field も消える
    rerender(
      <EffectEditor
        effect={{ ...emptyEffect(), action: 'exhaustSelf' }}
        meta={meta}
        allCardIds={[]}
        onChange={onChange}
        onRemove={() => {}}
      />,
    )
    expect(screen.queryByLabelText(/^effect amount$/i)).not.toBeInTheDocument()
  })

  it('changing action calls onChange', () => {
    const onChange = vi.fn()
    render(
      <EffectEditor
        effect={emptyEffect()}
        meta={meta}
        allCardIds={[]}
        onChange={onChange}
        onRemove={() => {}}
      />,
    )
    fireEvent.change(screen.getByLabelText(/effect action/i), {
      target: { value: 'draw' },
    })
    expect(onChange).toHaveBeenCalled()
    expect(onChange.mock.calls[0][0].action).toBe('draw')
  })

  it('edits an independent chance for an OnHit effect', () => {
    const onChange = vi.fn()
    render(
      <EffectEditor
        effect={{ ...emptyEffect(), action: 'debuff', trigger: 'OnHit', chancePercent: 25 }}
        meta={{ ...meta, effectActions: [...meta.effectActions, 'debuff'] }}
        allCardIds={[]}
        onChange={onChange}
        onRemove={() => {}}
      />,
    )

    const chance = screen.getByLabelText('effect chance percent')
    expect(chance).toHaveValue(25)
    fireEvent.change(chance, { target: { value: '40' } })
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ chancePercent: 40 }))
  })

  it('edits multiple debuffs as one grouped effect', () => {
    const onChange = vi.fn()
    const groupedMeta: DevMeta = {
      ...meta,
      effectActions: [...meta.effectActions, 'debuff'],
      statuses: [
        { id: 'weak', jp: '脱力', kind: 'Debuff' },
        { id: 'silence', jp: '沈黙', kind: 'Debuff' },
      ],
    }
    render(
      <EffectEditor
        effect={{
          ...emptyEffect(),
          action: 'debuff',
          scope: 'self',
          side: null,
          amount: 0,
          statusChanges: [{ name: 'weak', amount: 1 }],
        }}
        meta={groupedMeta}
        allCardIds={[]}
        onChange={onChange}
        onRemove={() => {}}
      />,
    )

    fireEvent.click(screen.getByRole('button', { name: '＋ ステータスを追加' }))
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      statusChanges: [
        { name: 'weak', amount: 1 },
        { name: 'silence', amount: 1 },
      ],
      name: null,
      amount: 0,
    }))
  })

  it('does not allow duplicate statuses in a grouped effect', () => {
    const groupedMeta: DevMeta = {
      ...meta,
      effectActions: [...meta.effectActions, 'debuff'],
      statuses: [
        { id: 'weak', jp: '脱力', kind: 'Debuff' },
        { id: 'silence', jp: '沈黙', kind: 'Debuff' },
      ],
    }
    render(
      <EffectEditor
        effect={{
          ...emptyEffect(), action: 'debuff', scope: 'self', side: null, amount: 0,
          statusChanges: [{ name: 'weak', amount: 1 }, { name: 'silence', amount: 1 }],
        }}
        meta={groupedMeta}
        allCardIds={[]}
        onChange={() => {}}
        onRemove={() => {}}
      />,
    )

    expect(screen.getByRole('button', { name: '＋ ステータスを追加' })).toBeDisabled()
    const firstStatus = screen.getByLabelText('status change 1')
    expect(within(firstStatus).getByRole('option', { name: '脱力' })).toBeTruthy()
    expect(within(firstStatus).queryByRole('option', { name: '沈黙' })).toBeNull()
  })

  it('shows pile, selection count, and duration for a card-cost modifier', () => {
    const onChange = vi.fn()
    render(
      <EffectEditor
        effect={{
          ...emptyEffect(),
          action: 'modifyCardCost',
          scope: 'self',
          amount: -1,
          pile: 'draw',
          select: 'choose',
          cardCount: 2,
          costDuration: 'untilPlayed',
        }}
        meta={meta}
        allCardIds={[]}
        onChange={onChange}
        onRemove={() => {}}
      />,
    )

    expect(screen.getByLabelText('effect card count')).toHaveValue(2)
    expect(screen.getByLabelText('effect cost duration')).toHaveValue('untilPlayed')
    fireEvent.change(screen.getByLabelText('effect cost duration'), {
      target: { value: 'untilTurnEnd' },
    })
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      costDuration: 'untilTurnEnd',
    }))
  })

  it.each(['draw', 'gainEnergy', 'gainComboGauge', 'gainMaxEnergy', 'modifyCardCost'])(
    'allows %s to target another allied participant',
    (action) => {
      const onChange = vi.fn()
      render(
        <EffectEditor
          effect={{
            ...emptyEffect(),
            action,
            scope: 'self',
            side: null,
            ...(action === 'modifyCardCost'
              ? {
                  amount: -1,
                  pile: 'hand',
                  select: 'all',
                  cardCount: 1,
                  costDuration: 'untilPlayed',
                }
              : {}),
          }}
          meta={{
            ...meta,
            effectActions: [...meta.effectActions, 'gainEnergy', 'gainMaxEnergy'],
          }}
          allCardIds={[]}
          onChange={onChange}
          onRemove={() => {}}
        />,
      )

      fireEvent.change(screen.getByLabelText('effect scope'), {
        target: { value: 'All' },
      })
      expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
        scope: 'All',
        side: 'ally',
      }))
      expect(screen.queryByRole('option', { name: /敵/ })).not.toBeInTheDocument()
      expect(screen.getByRole('option', { name: /味方/ })).toBeInTheDocument()
    },
  )

  it('offers currentCost only for card-cost modification and sets the literal amount to zero', () => {
    const onChange = vi.fn()
    const effect = {
      ...emptyEffect(),
      action: 'modifyCardCost',
      scope: 'self',
      amount: -1,
      pile: 'hand',
      select: 'all',
      costDuration: 'untilPlayed',
    }
    const view = render(
      <EffectEditor
        effect={effect}
        meta={meta}
        allCardIds={[]}
        onChange={onChange}
        onRemove={() => {}}
      />,
    )

    fireEvent.change(screen.getByLabelText('effect amount source'), {
      target: { value: 'currentCost' },
    })
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      amountSource: 'currentCost',
      amount: 0,
    }))

    view.rerender(
      <EffectEditor
        effect={{ ...effect, amount: 0, amountSource: 'currentCost' }}
        meta={meta}
        allCardIds={[]}
        onChange={onChange}
        onRemove={() => {}}
      />,
    )
    expect(screen.getByLabelText('effect amount')).toBeDisabled()

    view.rerender(
      <EffectEditor
        effect={emptyEffect()}
        meta={meta}
        allCardIds={[]}
        onChange={onChange}
        onRemove={() => {}}
      />,
    )
    expect(screen.queryByRole('option', { name: /対象カードの現在コスト/ })).toBeNull()
  })

  it('clears currentCost when changing to another action', () => {
    const onChange = vi.fn()
    render(
      <EffectEditor
        effect={{
          ...emptyEffect(), action: 'modifyCardCost', scope: 'self', amount: 0,
          amountSource: 'currentCost', pile: 'hand', select: 'all',
          costDuration: 'untilPlayed',
        }}
        meta={meta}
        allCardIds={[]}
        onChange={onChange}
        onRemove={() => {}}
      />,
    )

    fireEvent.change(screen.getByLabelText('effect action'), { target: { value: 'draw' } })
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      action: 'draw', amount: 1, amountSource: null,
    }))
  })

  it('restricts destination piles per recovery action', () => {
    render(
      <EffectEditor
        effect={{
          ...emptyEffect(), action: 'recoverFromDiscard', scope: 'self',
          pile: 'hand', select: 'choose', amount: 1,
        }}
        meta={meta}
        allCardIds={[]}
        onChange={() => {}}
        onRemove={() => {}}
      />,
    )

    const pile = screen.getByLabelText('effect pile')
    expect(within(pile).getByRole('option', { name: /手札/ })).toBeInTheDocument()
    expect(within(pile).getByRole('option', { name: /除外/ })).toBeInTheDocument()
    expect(within(pile).queryByRole('option', { name: /山札/ })).toBeNull()
    expect(within(pile).queryByRole('option', { name: /捨札/ })).toBeNull()
  })
})
