import { describe, expect, it, vi, afterEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import { RelicSpecForm } from './RelicSpecForm'
import { emptyRelicSpec } from './DevSpecTypes'
import type { DevMeta } from '../../api/dev'

// Phase 10.5.L1.5: relic-level Trigger dropdown 撤去 + effect-level trigger 表示。
// テストも対応 (relic trigger select の検証は削除)。

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
  // unified trigger list (Phase 10.5.L1.5 以降 18 値)
  triggers: [
    'OnPickup', 'OnBattleStart', 'OnBattleEnd',
    'OnTurnStart', 'OnTurnEnd', 'OnPlayCard',
    'OnEnemyDeath', 'OnDamageReceived', 'OnCombo',
    'OnMapTileResolved', 'OnCardDiscarded', 'OnCardExhausted',
    'OnEnterShop', 'OnEnterRestSite', 'OnRest',
    'OnRewardGenerated', 'OnCardAddedToDeck',
    'Passive',
  ],
  amountSources: ['handCount'],
  keywords: [{ id: 'wild', name: 'ワイルド', description: '...' }],
  statuses: [{ id: 'weak', jp: '脱力' }],
}

afterEach(() => {
  vi.restoreAllMocks()
})

describe('RelicSpecForm', () => {
  it('renders rarity / implemented fields (no relic-level trigger dropdown)', () => {
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
    expect(screen.getByLabelText(/relic implemented/i)).toBeInTheDocument()
    // relic-level Trigger dropdown は撤去された
    expect(screen.queryByLabelText(/relic trigger/i)).toBeNull()
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
