import { describe, expect, it, vi, afterEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { CardSpecForm } from './CardSpecForm'
import { emptySpec } from './DevSpecTypes'
import type { DevMeta } from '../../api/dev'

const meta: DevMeta = {
  cardTypes: ['Attack', 'Skill'],
  rarities: [
    { value: 1, label: 'Common' },
    { value: 2, label: 'Rare' },
  ],
  effectActions: ['attack', 'block', 'draw'],
  effectScopes: ['Self', 'Single', 'All'],
  effectSides: ['Enemy', 'Ally'],
  piles: ['hand', 'draw'],
  selectModes: ['random', 'choose'],
  triggers: ['OnTurnStart'],
  amountSources: ['handCount'],
  keywords: [{ id: 'wild', name: 'ワイルド', description: '...' }],
  statuses: [{ id: 'weak', jp: '脱力' }],
}

afterEach(() => {
  vi.restoreAllMocks()
})

describe('CardSpecForm', () => {
  it('renders rarity / cardType / cost fields', () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => ({ description: '' }),
    } as Response)
    render(
      <CardSpecForm
        spec={emptySpec()}
        meta={meta}
        allCardIds={['strike']}
        cardNames={{ strike: 'ストライク' }}
        cardName="テスト"
        displayName={null}
        onChange={() => {}}
      />,
    )
    expect(screen.getByLabelText(/card rarity/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/card type/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/^card cost$/i)).toBeInTheDocument()
  })

  it('changing rarity calls onChange with new value', () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => ({ description: '' }),
    } as Response)
    const onChange = vi.fn()
    render(
      <CardSpecForm
        spec={emptySpec()}
        meta={meta}
        allCardIds={[]}
        cardNames={{}}
        cardName="テスト"
        displayName={null}
        onChange={onChange}
      />,
    )
    fireEvent.change(screen.getByLabelText(/card rarity/i), {
      target: { value: '2' },
    })
    expect(onChange).toHaveBeenCalled()
    const arg = onChange.mock.calls[0][0]
    expect(arg.rarity).toBe(2)
  })

  it('shows + Add Effect button', () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => ({ description: '' }),
    } as Response)
    render(
      <CardSpecForm
        spec={emptySpec()}
        meta={meta}
        allCardIds={[]}
        cardNames={{}}
        cardName="テスト"
        displayName={null}
        onChange={() => {}}
      />,
    )
    expect(screen.getAllByRole('button', { name: /\+ Add Effect/i }).length).toBeGreaterThan(0)
  })
})
