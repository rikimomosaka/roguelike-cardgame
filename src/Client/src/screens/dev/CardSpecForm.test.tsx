import { describe, expect, it, vi, afterEach } from 'vitest'
import { render, screen, fireEvent, within } from '@testing-library/react'
import { CardSpecForm } from './CardSpecForm'
import { emptySpec } from './DevSpecTypes'
import type { DevMeta } from '../../api/dev'

const meta: DevMeta = {
  cardTypes: ['Attack', 'Skill', 'Power'],
  cardAbilities: ['finisher', 'reprise', 'contingency'],
  contingencyTriggerTimings: ['TurnStart', 'BeforeEnemyAttack', 'ComboReached'],
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
  statuses: [{ id: 'weak', jp: '脱力', kind: 'Debuff' }],
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
    expect(screen.getByLabelText(/^card combo gauge cost$/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/^card uses per turn$/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/^upgraded card uses per turn$/i)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /キーワード.*コピー/ })).not.toBeInTheDocument()
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

  it('shows + 効果を追加 button', () => {
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
    expect(screen.getAllByRole('button', { name: /\+ 効果を追加/ }).length).toBeGreaterThan(0)
  })

  it('edits combo gauge cost without dropping the field', () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => ({ description: '' }),
    } as Response)
    const onChange = vi.fn()
    render(
      <CardSpecForm
        spec={{ ...emptySpec(), comboGaugeCost: 6 }}
        meta={meta}
        allCardIds={[]}
        cardNames={{}}
        cardName="テスト"
        displayName={null}
        onChange={onChange}
      />,
    )

    expect(screen.getByLabelText('card combo gauge cost')).toHaveValue(6)
    fireEvent.change(screen.getByLabelText('card combo gauge cost'), { target: { value: '9' } })
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ comboGaugeCost: 9 }))
  })

  it('authors normal and upgraded typed abilities independently', () => {
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

    const normal = screen.getByRole('region', { name: '通常版の固有能力' })
    fireEvent.click(within(normal).getByRole('button', { name: '＋ 再演' }))
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      abilities: [{
        type: 'reprise',
        filter: expect.objectContaining({ cardTypes: ['Skill'], maxOriginalCost: 3 }),
      }],
      upgradedAbilities: null,
    }))

    const upgraded = screen.getByRole('region', { name: '強化版の固有能力' })
    fireEvent.click(within(upgraded).getByRole('button', { name: '＋ 伏策' }))
    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({
      abilities: null,
      upgradedAbilities: [{
        type: 'contingency',
        trigger: { timing: 'TurnStart', comboThreshold: null },
      }],
    }))
  })

  it('prevents typed abilities from being authored on unsupported card types', () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue({
      ok: true,
      json: async () => ({ description: '' }),
    } as Response)
    const spec = { ...emptySpec(), abilities: [{ type: 'finisher' as const }] }
    const view = render(
      <CardSpecForm
        spec={spec}
        meta={meta}
        allCardIds={[]}
        cardNames={{}}
        cardName="テスト"
        displayName={null}
        onChange={() => {}}
      />,
    )

    expect(screen.getByRole('option', { name: 'Power' })).toBeDisabled()

    view.rerender(
      <CardSpecForm
        spec={{ ...emptySpec(), cardType: 'Power' }}
        meta={meta}
        allCardIds={[]}
        cardNames={{}}
        cardName="テスト"
        displayName={null}
        onChange={() => {}}
      />,
    )
    const normal = screen.getByRole('region', { name: '通常版の固有能力' })
    expect(within(normal).getByText('固有能力はアタックまたはスキルにのみ設定できます。')).toBeTruthy()
    expect(within(normal).queryByRole('button', { name: /＋/ })).toBeNull()
  })
})
