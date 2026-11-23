import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchCharacterCatalog, getCardCatalog } from '../api/catalog'
import { startNewRun } from '../api/runs'
import type { RunSnapshotDto } from '../api/types'
import { TooltipHost } from '../components/Tooltip'
import { CharacterPickScreen } from './CharacterPickScreen'

vi.mock('../api/catalog', () => ({
  fetchCharacterCatalog: vi.fn(),
  fetchUnitCatalog: vi.fn().mockResolvedValue({}),
  getCardCatalog: vi.fn(),
}))

vi.mock('../api/runs', () => ({
  startNewRun: vi.fn(),
}))

const characters = {
  default: {
    id: 'default',
    name: '冒険者',
    maxHp: 80,
    startingGold: 99,
    potionSlotCount: 3,
    heightTier: 1,
    characterClass: 'Striker' as const,
    deck: ['strike', 'strike', 'defend'],
    description: '攻撃を得意とする冒険者。',
    traitName: '連閃',
    traitDescription: '最大コンボ4で残光を得る。',
    traitEffects: '自身の最大コンボが[N:4]以上になった時、自身に[S:afterimage][N:1]を付与する。',
    energyPerTurn: 3,
    drawPerTurn: 5,
    skills: [
      {
        id: 'tactical',
        name: '刃風展開',
        energyCost: 0,
        comboGaugeCost: 3,
        oncePerTurn: true,
        usesPerTurn: 1,
        description: '[N:1]枚引く。自身に[S:omnistrike][N:1]を付与する。',
        requiresCardsInHand: false,
      },
      {
        id: 'ultimate',
        name: '一気呵成',
        energyCost: 0,
        comboGaugeCost: 6,
        oncePerTurn: true,
        usesPerTurn: 1,
        description: '自身に[S:vigor][N:1]、[S:afterimage][N:1]を付与する。',
        requiresCardsInHand: false,
      },
    ],
  },
}

const cards = {
  strike: {
    id: 'strike',
    name: 'strike',
    displayName: 'ストライク',
    rarity: 0,
    cardType: 'Attack',
    cost: 1,
    upgradedCost: 1,
    upgradable: true,
    description: '敵単体に6アタック。',
    upgradedDescription: '敵単体に9アタック。',
  },
  defend: {
    id: 'defend',
    name: 'defend',
    displayName: '防御',
    rarity: 0,
    cardType: 'Skill',
    cost: 1,
    upgradedCost: 1,
    upgradable: true,
    description: '自身に5ブロック。',
    upgradedDescription: '自身に8ブロック。',
  },
}

const snapshot = {
  run: { schemaVersion: 8, progress: 'InProgress', currentNodeId: 0 },
  map: { startNodeId: 0, bossNodeId: 1, nodes: [] },
} as unknown as RunSnapshotDto

function renderPicker(overrides: Partial<React.ComponentProps<typeof CharacterPickScreen>> = {}) {
  const props: React.ComponentProps<typeof CharacterPickScreen> = {
    accountId: 'alice',
    force: false,
    onStarted: vi.fn(),
    onBack: vi.fn(),
    ...overrides,
  }
  return {
    props,
    ...render(
      <TooltipHost>
        <CharacterPickScreen {...props} />
      </TooltipHost>,
    ),
  }
}

describe('CharacterPickScreen', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(fetchCharacterCatalog).mockResolvedValue(characters)
    vi.mocked(getCardCatalog).mockResolvedValue(cards)
    vi.mocked(startNewRun).mockResolvedValue(snapshot)
  })

  it('shows character identity, class, descriptions, and aggregated starting deck', async () => {
    const { container } = renderPicker()

    expect(await screen.findByRole('button', { name: '冒険者を選択' })).toBeInTheDocument()
    // 能力値: 体力・行動力・知力・攻撃力・守備力。
    expect(container.querySelector('.actor-stats__item--hp')).toHaveTextContent('体力80')
    expect(container.querySelector('.actor-stats__item--energyPerTurn')).toHaveTextContent('行動力3')
    expect(container.querySelector('.actor-stats__item--drawPerTurn')).toHaveTextContent('知力5')
    expect(container.querySelector('.actor-stats__item--attack')).toHaveTextContent('攻撃力')
    expect(container.querySelector('.actor-stats__item--defense')).toHaveTextContent('守備力')
    expect(screen.getByText('攻撃を得意とする冒険者。')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: '特性：連閃' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: '戦技：刃風展開' })).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: '奥義：一気呵成' })).toBeInTheDocument()
    expect(document.querySelector('[data-term="afterimage"]')).toBeInTheDocument()
    expect(document.querySelector('.character-pick__details')).toHaveTextContent(
      '自身の最大コンボが4以上になった時、自身に残光1を付与する。',
    )
    expect(document.querySelector('.character-pick__details')).not.toHaveTextContent('最大コンボ4で残光を得る。')
    expect(document.querySelector('.character-pick__details')).toHaveTextContent('コンボゲージ 3')
    expect(screen.getByText('ストライク')).toBeInTheDocument()
    expect(screen.getByText('×2')).toBeInTheDocument()
    expect(screen.getByText('防御')).toBeInTheDocument()
    expect(container.querySelector('.character-pick__portrait img')).toHaveAttribute(
      'src', '/characters/portraits/default.png',
    )
    expect(screen.getByRole('button', { name: '冒険者を選択' })).toHaveClass(
      'character-pick__select',
    )
  })

  it('shows keyword and status explanations from trait and skill rich text', async () => {
    renderPicker()
    await screen.findByRole('button', { name: '冒険者を選択' })

    const afterimage = document.querySelector<HTMLElement>('[data-term="afterimage"]')
    expect(afterimage).not.toBeNull()
    fireEvent.mouseEnter(afterimage!)

    const tooltip = screen.getByRole('tooltip')
    expect(within(tooltip).getAllByText('残光').length).toBeGreaterThan(0)
    expect(tooltip).toHaveTextContent('終撃によって保存された全ての効果')
  })

  it('opens the starting deck as inspectable cards and closes the pile modal', async () => {
    renderPicker()
    await screen.findByRole('button', { name: '冒険者を選択' })

    fireEvent.click(screen.getByRole('button', { name: '初期デッキ（3枚）を表示' }))

    const dialog = screen.getByRole('dialog', { name: '冒険者の初期デッキ (3枚)' })
    expect(dialog.querySelector('.character-pick__deck-modal-list')).toBeInTheDocument()
    expect(within(dialog).getAllByText('ストライク')).toHaveLength(2)
    expect(within(dialog).getByText('防御')).toBeInTheDocument()

    fireEvent.click(within(dialog).getByRole('button', { name: '閉じる' }))
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('keeps unimplemented class tabs visible with an explicit empty state', async () => {
    renderPicker()
    await screen.findByRole('button', { name: '冒険者を選択' })

    fireEvent.click(screen.getByRole('tab', { name: /ガーディアン/ }))

    expect(screen.getByText('このクラスのキャラクターはまだ実装されていません。')).toBeInTheDocument()
  })

  it('starts the selected character with the preserved overwrite flag', async () => {
    const onStarted = vi.fn()
    renderPicker({ force: true, onStarted })
    await screen.findByRole('button', { name: '冒険者を選択' })

    fireEvent.click(screen.getByRole('button', { name: /冒険者/ }))
    fireEvent.click(screen.getByRole('button', { name: 'このキャラクターで開始' }))

    await waitFor(() => expect(startNewRun).toHaveBeenCalledWith('alice', true, ['default']))
    expect(onStarted).toHaveBeenCalledWith(snapshot)
  })

  it('prevents a second start request while the first request is pending', async () => {
    let resolveStart!: (value: RunSnapshotDto) => void
    vi.mocked(startNewRun).mockReturnValue(new Promise(resolve => { resolveStart = resolve }))
    renderPicker()
    await screen.findByRole('button', { name: '冒険者を選択' })
    fireEvent.click(screen.getByRole('button', { name: /冒険者/ }))

    const startButton = screen.getByRole('button', { name: 'このキャラクターで開始' })
    fireEvent.click(startButton)
    fireEvent.click(startButton)

    expect(startNewRun).toHaveBeenCalledTimes(1)
    resolveStart(snapshot)
    await waitFor(() => expect(startButton).toBeEnabled())
  })

  it('offers retry after catalog loading fails', async () => {
    vi.mocked(fetchCharacterCatalog).mockRejectedValueOnce(new Error('offline'))
    renderPicker()

    expect(await screen.findByRole('alert')).toHaveTextContent('キャラクターデータを読み込めませんでした。')
    fireEvent.click(screen.getByRole('button', { name: '再試行' }))

    expect(await screen.findByRole('button', { name: '冒険者を選択' })).toBeInTheDocument()
    expect(fetchCharacterCatalog).toHaveBeenCalledTimes(2)
  })

  it('returns without creating a run', async () => {
    const onBack = vi.fn()
    renderPicker({ onBack })
    await screen.findByRole('button', { name: '冒険者を選択' })

    fireEvent.click(screen.getByRole('button', { name: '戻る' }))

    expect(onBack).toHaveBeenCalledOnce()
    expect(startNewRun).not.toHaveBeenCalled()
  })
})
