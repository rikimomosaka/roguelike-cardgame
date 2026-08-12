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
})
