import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { EventInstanceDto } from '../api/types'
import { EventScreen } from './EventScreen'

const ev: EventInstanceDto = {
  eventId: 'shady_merchant',
  name: 'Shady Merchant',
  startMessage: 'A suspicious figure offers...',
  choices: [
    { label: '購入する(50ゴールド支払う)', conditionSummary: 'requires 50 gold', conditionMet: true, resultMessage: 'You bought a relic.' },
    { label: '一番いいのを頼む(200ゴールド支払う)', conditionSummary: 'requires 200 gold', conditionMet: true, resultMessage: 'You bought the finest relic.' },
    { label: '立ち去る', conditionSummary: null, conditionMet: true, resultMessage: 'You walked away.' },
  ],
  chosenIndex: null,
}

describe('EventScreen', () => {
  it('name / startMessage / 全選択肢を表示する', () => {
    render(<EventScreen event={ev} onChoose={vi.fn()} onClose={vi.fn()} />)
    expect(screen.getByText('Shady Merchant')).toBeDefined()
    expect(screen.getByText(/suspicious figure/)).toBeDefined()
    expect(screen.getByRole('button', { name: '購入する(50ゴールド支払う)' })).toBeDefined()
    expect(screen.getByRole('button', { name: '一番いいのを頼む(200ゴールド支払う)' })).toBeDefined()
    expect(screen.getByRole('button', { name: '立ち去る' })).toBeDefined()
    expect(screen.queryByText(/requires/)).toBeNull()
  })

  it('conditionMet=false のとき選択肢は disabled', () => {
    const locked: EventInstanceDto = {
      ...ev,
      choices: [{ ...ev.choices[0], conditionMet: false }, ev.choices[1]],
    }
    render(<EventScreen event={locked} onChoose={vi.fn()} onClose={vi.fn()} />)
    const btn = screen.getByRole('button', { name: '購入する(50ゴールド支払う)' }) as HTMLButtonElement
    expect(btn.disabled).toBe(true)
  })

  it('onChoose が選択肢インデックスで呼ばれる', async () => {
    const onChoose = vi.fn()
    render(<EventScreen event={ev} onChoose={onChoose} onClose={vi.fn()} />)
    fireEvent.click(screen.getByRole('button', { name: '立ち去る' }))
    await waitFor(() => expect(onChoose).toHaveBeenCalledWith(2))
  })

  it('選択後は選んだ選択肢の resultMessage を表示する', () => {
    const chosen: EventInstanceDto = { ...ev, chosenIndex: 2 }
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

  it('選択処理中は全選択肢をロックして二重送信しない', async () => {
    let finish!: () => void
    const onChoose = vi.fn(() => new Promise<void>(resolve => { finish = resolve }))
    render(<EventScreen event={ev} onChoose={onChoose} onClose={vi.fn()} />)

    const first = screen.getByRole('button', { name: '購入する(50ゴールド支払う)' })
    fireEvent.click(first)
    fireEvent.click(screen.getByRole('button', { name: '立ち去る' }))
    expect(onChoose).toHaveBeenCalledTimes(1)
    expect(screen.getByText('運命を確定中…')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Close' })).toBeDisabled()

    await act(async () => finish())
  })
})
