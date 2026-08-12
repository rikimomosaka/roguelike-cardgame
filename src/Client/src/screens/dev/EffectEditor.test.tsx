import { describe, expect, it, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { EffectEditor } from './EffectEditor'
import { emptyEffect } from './DevSpecTypes'
import type { DevMeta } from '../../api/dev'

const meta: DevMeta = {
  cardTypes: [],
  rarities: [],
  effectActions: ['attack', 'draw', 'exhaustSelf', 'modifyCardCost'],
  effectScopes: ['Self', 'Single', 'All'],
  effectSides: ['Enemy', 'Ally'],
  piles: ['hand', 'draw', 'discard', 'exhaust'],
  selectModes: ['random', 'choose', 'all'],
  triggers: ['OnTurnStart', 'OnHit'],
  amountSources: ['handCount'],
  keywords: [],
  statuses: [{ id: 'weak', jp: '脱力' }],
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
    expect(screen.getByLabelText(/effect amount/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/effect action/i)).toBeInTheDocument()
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
    expect(screen.queryByLabelText(/effect amount/i)).toBeInTheDocument()

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
    expect(screen.queryByLabelText(/effect amount/i)).not.toBeInTheDocument()
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
})
