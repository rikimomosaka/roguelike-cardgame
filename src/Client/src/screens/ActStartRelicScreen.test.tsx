import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ActStartRelicScreen } from './ActStartRelicScreen'

describe('ActStartRelicScreen', () => {
  it('renders 3 relic buttons and calls onChoose', () => {
    const onChoose = vi.fn()
    render(
      <ActStartRelicScreen
        choices={['r1', 'r2', 'r3']}
        relicNames={{ r1: 'Relic 1', r2: 'Relic 2', r3: 'Relic 3' }}
        onChoose={onChoose}
      />,
    )
    const buttons = screen.getAllByRole('button', { name: /Relic \d/i })
    expect(buttons).toHaveLength(3)
    fireEvent.click(buttons[1])
    expect(onChoose).toHaveBeenCalledWith('r2')
  })

  it('has dialog role', () => {
    render(
      <ActStartRelicScreen choices={['r1', 'r2', 'r3']} relicNames={{}} onChoose={vi.fn()} />,
    )
    const dlg = screen.getByRole('dialog')
    expect(dlg.getAttribute('aria-modal')).toBe('true')
  })
})
