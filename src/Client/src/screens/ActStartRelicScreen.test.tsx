import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ActStartRelicScreen } from './ActStartRelicScreen'

describe('ActStartRelicScreen', () => {
  it('renders 3 relic buttons and calls onChoose', () => {
    const onChoose = vi.fn().mockResolvedValue(undefined)
    render(
      <ActStartRelicScreen
        choices={['r1', 'r2', 'r3']}
        relicNames={{ r1: 'Relic 1', r2: 'Relic 2', r3: 'Relic 3' }}
        onChoose={onChoose}
        onClose={vi.fn()}
      />,
    )
    const buttons = screen.getAllByRole('button', { name: /Relic \d/i })
    expect(buttons).toHaveLength(3)
    fireEvent.click(buttons[1])
    expect(onChoose).toHaveBeenCalledWith('r2')
  })

  it('has dialog role', () => {
    render(
      <ActStartRelicScreen
        choices={['r1', 'r2', 'r3']}
        relicNames={{}}
        onChoose={vi.fn()}
        onClose={vi.fn()}
      />,
    )
    const dlg = screen.getByRole('dialog')
    expect(dlg.getAttribute('aria-modal')).toBe('true')
  })

  it('does not auto-close on choose; user must click 閉じる to close', async () => {
    const onChoose = vi.fn().mockResolvedValue(undefined)
    const onClose = vi.fn()
    render(
      <ActStartRelicScreen
        choices={['r1', 'r2', 'r3']}
        relicNames={{ r1: 'A', r2: 'B', r3: 'C' }}
        onChoose={onChoose}
        onClose={onClose}
      />,
    )
    fireEvent.click(screen.getByRole('button', { name: 'B' }))
    await waitFor(() => expect(onChoose).toHaveBeenCalledWith('r2'))
    // onClose は自動的には呼ばれない (手動クローズ専用)
    expect(onClose).not.toHaveBeenCalled()
    // 閉じるボタンを明示的に押す
    fireEvent.click(screen.getByRole('button', { name: '閉じる' }))
    expect(onClose).toHaveBeenCalled()
  })

  it('renders generic title "レリックを選ぶ"', () => {
    render(
      <ActStartRelicScreen
        choices={['r1']}
        relicNames={{ r1: 'A' }}
        onChoose={vi.fn()}
        onClose={vi.fn()}
      />,
    )
    expect(screen.getByText('レリックを選ぶ')).toBeDefined()
  })
})
