import { fireEvent, render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { TooltipHost, useTooltipTarget } from './Tooltip'

vi.mock('../hooks/useCardCatalog', () => ({
  useCardCatalog: () => ({ names: {} }),
}))

vi.mock('../hooks/useUnitCatalog', () => ({
  useUnitCatalog: () => ({ names: {} }),
}))

function TooltipTrigger({ withStatusLine = true }: { withStatusLine?: boolean }) {
  const tip = useTooltipTarget({
    name: 'コンボ',
    statusLine: withStatusLine ? '(このターンの最大コンボ:4)' : undefined,
    desc: '現在1コンボ。コスト2のカードをプレイする時、1コスト軽減してコンボ継続。',
  })

  return <button {...tip}>説明を表示</button>
}

function StatusTooltipTrigger() {
  const tip = useTooltipTarget({
    name: '加速',
    desc: 'ターン開始時のエナジー回復量を+[N:X]し、[S:haste]が消滅する。[S:strength]を得る。',
    selfReference: { kind: 'status', id: 'haste' },
  })
  return <button {...tip}>状態説明を表示</button>
}

describe('Tooltip status line', () => {
  it('renders title, optional status, and description in that DOM order', () => {
    render(
      <TooltipHost>
        <TooltipTrigger />
      </TooltipHost>,
    )

    const trigger = screen.getByRole('button', { name: '説明を表示' })
    fireEvent.mouseEnter(trigger, { clientX: 20, clientY: 20 })

    const tooltip = screen.getByRole('tooltip')
    const title = within(tooltip).getByText('コンボ')
    const status = within(tooltip).getByText('(このターンの最大コンボ:4)')
    const description = within(tooltip).getByText(/現在1コンボ。/)

    expect(title.compareDocumentPosition(status) & Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(0)
    expect(status.compareDocumentPosition(description) & Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(0)
    expect(trigger).toHaveAttribute('aria-describedby', tooltip.id)
  })

  it('keeps existing tooltip content valid when no status line is supplied', () => {
    render(
      <TooltipHost>
        <TooltipTrigger withStatusLine={false} />
      </TooltipHost>,
    )

    fireEvent.focus(screen.getByRole('button', { name: '説明を表示' }))

    const tooltip = screen.getByRole('tooltip')
    expect(within(tooltip).queryByText(/このターンの最大コンボ/)).not.toBeInTheDocument()
    expect(within(tooltip).getByText(/現在1コンボ。/)).toBeInTheDocument()
  })

  it('highlights markers without repeating the same status definition panel', () => {
    render(
      <TooltipHost>
        <StatusTooltipTrigger />
      </TooltipHost>,
    )

    fireEvent.focus(screen.getByRole('button', { name: '状態説明を表示' }))
    const tooltip = screen.getByRole('tooltip')
    expect(tooltip.querySelector('.card-desc-num')).toHaveTextContent('X')
    expect(tooltip.querySelector('.card-desc-status')).toHaveTextContent('加速')
    const refs = tooltip.querySelectorAll('.tip__ref')
    expect(refs).toHaveLength(1)
    expect(refs[0]).toHaveTextContent('筋力')
    expect(refs[0]).not.toHaveTextContent('加速')
  })

  it('shows keyword explanations in a separate side panel, not under the description', () => {
    render(
      <TooltipHost>
        <StatusTooltipTrigger />
      </TooltipHost>,
    )

    fireEvent.mouseEnter(screen.getByRole('button', { name: '状態説明を表示' }), { clientX: 20, clientY: 20 })
    const tooltip = screen.getByRole('tooltip')
    const side = tooltip.querySelector('.tip__side')
    expect(side).toBeInTheDocument()
    expect(side?.querySelector('.tip__refs-panel .tip__ref')).toHaveTextContent('筋力')
    expect(tooltip.querySelector('.tip__desc .tip__ref')).toBeNull()
    // カーソルの右に出る本文の、さらに右に並べる。
    expect(tooltip).toHaveAttribute('data-side', 'right')
  })

  it('has no side panel when there is nothing to explain', () => {
    render(
      <TooltipHost>
        <TooltipTrigger />
      </TooltipHost>,
    )

    fireEvent.mouseEnter(screen.getByRole('button', { name: '説明を表示' }), { clientX: 20, clientY: 20 })
    expect(screen.getByRole('tooltip').querySelector('.tip__side')).toBeNull()
  })
})
