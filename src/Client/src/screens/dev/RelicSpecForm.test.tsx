import { describe, expect, it, vi, afterEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { RelicSpecForm } from './RelicSpecForm'
import { emptyRelicSpec } from './DevSpecTypes'
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
  relicTriggers: ['OnPickup', 'Passive', 'OnBattleStart'],
}

afterEach(() => {
  vi.restoreAllMocks()
})

describe('RelicSpecForm', () => {
  it('renders rarity / trigger / implemented fields', () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => ({ description: '' }),
    } as Response)
    render(
      <RelicSpecForm
        relicId="test_relic"
        relicName="テストレリック"
        spec={emptyRelicSpec()}
        meta={meta}
        allCardIds={[]}
        onChange={() => {}}
      />,
    )
    expect(screen.getByLabelText(/relic rarity/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/relic trigger/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/relic implemented/i)).toBeInTheDocument()
  })

  it('changing trigger calls onChange with new value', () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => ({ description: '' }),
    } as Response)
    const onChange = vi.fn()
    render(
      <RelicSpecForm
        relicId="test_relic"
        relicName="テストレリック"
        spec={emptyRelicSpec()}
        meta={meta}
        allCardIds={[]}
        onChange={onChange}
      />,
    )
    fireEvent.change(screen.getByLabelText(/relic trigger/i), {
      target: { value: 'Passive' },
    })
    expect(onChange).toHaveBeenCalled()
    const arg = onChange.mock.calls[0][0]
    expect(arg.trigger).toBe('Passive')
  })

  it('shows description override textarea', () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => ({ description: '' }),
    } as Response)
    render(
      <RelicSpecForm
        relicId="test_relic"
        relicName="テストレリック"
        spec={emptyRelicSpec()}
        meta={meta}
        allCardIds={[]}
        onChange={() => {}}
      />,
    )
    expect(screen.getByLabelText(/relic description override/i)).toBeInTheDocument()
  })
})
