import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { EventInstanceDto } from '../api/types'
import { EventScreen } from './EventScreen'

const ev: EventInstanceDto = {
  eventId: 'shady_merchant',
  name: 'Shady Merchant',
  startMessage: 'A suspicious figure offers...',
  choices: [
    { label: 'Pay 50 gold for a relic', conditionSummary: 'requires 50 gold', conditionMet: true, resultMessage: 'You bought a relic.' },
    { label: 'Walk away', conditionSummary: null, conditionMet: true, resultMessage: 'You walked away.' },
  ],
  chosenIndex: null,
}

describe('EventScreen', () => {
  it('name / startMessage / 全選択肢を表示する', () => {
    render(<EventScreen event={ev} onChoose={vi.fn()} onClose={vi.fn()} />)
    expect(screen.getByText('Shady Merchant')).toBeDefined()
    expect(screen.getByText(/suspicious figure/)).toBeDefined()
    expect(screen.getByRole('button', { name: /Pay 50 gold/ })).toBeDefined()
    expect(screen.getByRole('button', { name: /Walk away/ })).toBeDefined()
  })

  it('conditionMet=false のとき選択肢は disabled', () => {
    const locked: EventInstanceDto = {
      ...ev,
      choices: [{ ...ev.choices[0], conditionMet: false }, ev.choices[1]],
    }
    render(<EventScreen event={locked} onChoose={vi.fn()} onClose={vi.fn()} />)
    const btn = screen.getByRole('button', { name: /Pay 50 gold/ }) as HTMLButtonElement
    expect(btn.disabled).toBe(true)
  })

  it('onChoose が選択肢インデックスで呼ばれる', async () => {
    const onChoose = vi.fn()
    render(<EventScreen event={ev} onChoose={onChoose} onClose={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: /Walk away/ }))
    await waitFor(() => expect(onChoose).toHaveBeenCalledWith(1))
  })

  it('選択後は選んだ選択肢の resultMessage を表示する', () => {
    const chosen: EventInstanceDto = { ...ev, chosenIndex: 1 }
    render(<EventScreen event={chosen} onChoose={vi.fn()} onClose={vi.fn()} />)
    expect(screen.getByText('You walked away.')).toBeDefined()
  })

  it('常に閉じるボタンが押せる（未選択でも）', () => {
    const onClose = vi.fn()
    render(<EventScreen event={ev} onChoose={vi.fn()} onClose={onClose} />)
    const close = screen.getByRole('button', { name: 'Close' }) as HTMLButtonElement
    expect(close.disabled).toBe(false)
    fireEvent.click(close)
    expect(onClose).toHaveBeenCalled()
  })
})
