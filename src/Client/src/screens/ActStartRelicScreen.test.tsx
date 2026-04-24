import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ActStartRelicScreen } from './ActStartRelicScreen'

describe('ActStartRelicScreen', () => {
  it('renders 3 relic buttons', () => {
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

  it('クリックでレリックを選択状態にする（aria-pressed=true）', () => {
    render(
      <ActStartRelicScreen
        choices={['r1', 'r2']}
        relicNames={{ r1: 'A', r2: 'B' }}
        onChoose={vi.fn()}
        onClose={vi.fn()}
      />,
    )
    const a = screen.getByRole('button', { name: 'A' })
    fireEvent.click(a)
    expect(a.getAttribute('aria-pressed')).toBe('true')
  })

  it('別のレリックをクリックすると選択が切り替わる', () => {
    render(
      <ActStartRelicScreen
        choices={['r1', 'r2']}
        relicNames={{ r1: 'A', r2: 'B' }}
        onChoose={vi.fn()}
        onClose={vi.fn()}
      />,
    )
    const a = screen.getByRole('button', { name: 'A' })
    const b = screen.getByRole('button', { name: 'B' })
    fireEvent.click(a)
    fireEvent.click(b)
    expect(a.getAttribute('aria-pressed')).toBe('false')
    expect(b.getAttribute('aria-pressed')).toBe('true')
  })

  it('選択中のレリックをもう一度クリックすると解除される', () => {
    render(
      <ActStartRelicScreen
        choices={['r1']}
        relicNames={{ r1: 'A' }}
        onChoose={vi.fn()}
        onClose={vi.fn()}
      />,
    )
    const a = screen.getByRole('button', { name: 'A' })
    fireEvent.click(a)
    fireEvent.click(a)
    expect(a.getAttribute('aria-pressed')).toBe('false')
  })

  it('未選択時は閉じるボタン、選択中は決定ボタンが出る', () => {
    render(
      <ActStartRelicScreen
        choices={['r1']}
        relicNames={{ r1: 'A' }}
        onChoose={vi.fn()}
        onClose={vi.fn()}
      />,
    )
    expect(screen.getByRole('button', { name: '閉じる' })).toBeDefined()
    fireEvent.click(screen.getByRole('button', { name: 'A' }))
    expect(screen.queryByRole('button', { name: '閉じる' })).toBeNull()
    expect(screen.getByRole('button', { name: '決定' })).toBeDefined()
  })

  it('決定を押すと選んでいた relic で onChoose → onClose が呼ばれる', async () => {
    const onChoose = vi.fn().mockResolvedValue(undefined)
    const onClose = vi.fn()
    render(
      <ActStartRelicScreen
        choices={['r1', 'r2']}
        relicNames={{ r1: 'A', r2: 'B' }}
        onChoose={onChoose}
        onClose={onClose}
      />,
    )
    fireEvent.click(screen.getByRole('button', { name: 'B' }))
    fireEvent.click(screen.getByRole('button', { name: '決定' }))
    await waitFor(() => expect(onChoose).toHaveBeenCalledWith('r2'))
    await waitFor(() => expect(onClose).toHaveBeenCalled())
  })

  it('閉じる（未選択）を押しても onChoose は呼ばれない', () => {
    const onChoose = vi.fn()
    const onClose = vi.fn()
    render(
      <ActStartRelicScreen
        choices={['r1']}
        relicNames={{ r1: 'A' }}
        onChoose={onChoose}
        onClose={onClose}
      />,
    )
    fireEvent.click(screen.getByRole('button', { name: '閉じる' }))
    expect(onChoose).not.toHaveBeenCalled()
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
