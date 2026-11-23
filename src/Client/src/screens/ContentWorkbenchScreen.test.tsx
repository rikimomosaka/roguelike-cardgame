import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const api = vi.hoisted(() => ({
  deleteContentOverride: vi.fn(),
  fetchContentDocument: vi.fn(),
  fetchContentList: vi.fn(),
  fetchContentMeta: vi.fn(),
  previewContentDocument: vi.fn(),
  saveContentAsStandardV1: vi.fn(),
  saveContentDocument: vi.fn(),
  validateContentDocument: vi.fn(),
}))

vi.mock('../api/devContent', () => api)

import { ContentWorkbenchScreen } from './ContentWorkbenchScreen'
import { TooltipHost } from '../components/Tooltip'

const items = [
  { id: 'card_a', name: 'カードA', hasBase: true, hasOverride: false, revision: 'r1', poolClass: 'Striker' },
  { id: 'card_b', name: 'カードB', hasBase: true, hasOverride: false, revision: 'r1', poolClass: 'Universal' },
  { id: 'card_c', name: 'カードC', hasBase: true, hasOverride: false, revision: 'r1', poolClass: null },
]

const meta = {
  readOnly: false,
  kinds: [{
    id: 'cards', label: 'カード', group: 'プレイヤー', sourceDirectory: 'Cards',
    overrideDirectory: 'cards', canCreate: true, canDelete: true,
    supportsGamePreview: true, limitation: null,
  }],
  enums: {
    cardTypes: ['Skill'], rarities: [1], scopes: ['self'], sides: ['ally'],
    characterClasses: ['Striker', 'Guardian', 'Tactician', 'Enhancer'],
    cardPoolClasses: ['Universal', 'Striker', 'Guardian', 'Tactician', 'Enhancer'],
    cardPoolKinds: ['Basic', 'Reward'],
    cardAbilities: ['finisher', 'reprise', 'contingency'],
    contingencyTriggerTimings: ['TurnStart', 'BeforeEnemyAttack', 'ComboReached'],
    characterTraitTriggers: ['OnTurnStart', 'OnComboReached', 'OnContingencyTriggered', 'OnResonanceConsumed'],
    selects: ['choose'], piles: ['hand'], tiers: ['Weak'], triggers: ['OnPlayCard'],
    actions: ['draw'], amountSources: ['handCount'], eventEffectTypes: ['heal', 'gainCard'],
    eventConditionTypes: ['minHp'], eventRarities: ['common'], tileKinds: ['Enemy'],
    optionLabels: {
      'cardTypes:Skill': 'スキル', 'rarities:1': 'コモン',
      'characterClasses:Striker': 'ストライカー', 'cardPoolClasses:Striker': 'ストライカー',
      'characterClasses:Guardian': 'ガーディアン', 'cardPoolClasses:Guardian': 'ガーディアン',
      'characterClasses:Tactician': 'タクティシャン', 'cardPoolClasses:Tactician': 'タクティシャン',
      'characterClasses:Enhancer': 'エンハンサー', 'cardPoolClasses:Enhancer': 'エンハンサー',
      'cardPoolClasses:Universal': 'ユニバーサル',
      'cardPoolKinds:Basic': '基本カード', 'cardPoolKinds:Reward': '報酬カード',
    },
    statuses: [], keywords: [],
  },
}

function documentFor(id: string, displayName = id) {
  return {
    kind: 'cards', id, name: displayName,
    document: {
      id,
      displayName,
      activeVersion: 'v1',
      versions: [{ version: 'v1', spec: { rarity: 1, cardType: 'Skill', cost: 1, effects: [] } }],
    },
    hasBase: true, hasOverride: false, revision: 'r1', referencedBy: [],
  }
}

describe('ContentWorkbenchScreen', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.fetchContentMeta.mockResolvedValue(meta)
    api.fetchContentList.mockResolvedValue(items)
    api.fetchContentDocument.mockImplementation((_kind: string, id: string) =>
      Promise.resolve(documentFor(id, id === 'card_a' ? 'カードA' : 'カードB')))
    api.previewContentDocument.mockResolvedValue({ description: '' })
    api.validateContentDocument.mockResolvedValue({ valid: true })
    api.saveContentDocument.mockImplementation((_kind: string, id: string, document: Record<string, unknown>) =>
      Promise.resolve({ ...documentFor(id), document, hasOverride: true, revision: 'r2' }))
    api.saveContentAsStandardV1.mockImplementation((_kind: string, id: string) =>
      Promise.resolve(documentFor(id)))
  })

  it('clears a successful save message as soon as another document is selected', async () => {
    render(<ContentWorkbenchScreen initialKind="cards" onClose={() => {}} />)

    const displayName = await screen.findByLabelText('表示名')
    fireEvent.change(displayName, { target: { value: '保存後カードA' } })
    fireEvent.click(screen.getByRole('button', { name: '保存して反映' }))
    expect(await screen.findByText('編集用上書きへ保存し、実ゲームへ反映しました。')).toBeTruthy()

    fireEvent.click(screen.getByText('カードB'))
    expect(screen.queryByText('編集用上書きへ保存し、実ゲームへ反映しました。')).toBeNull()
    await waitFor(() => expect(api.fetchContentDocument).toHaveBeenCalledWith('cards', 'card_b'))
  })

  it('shows the same status explanation when a preview term is hovered', async () => {
    api.previewContentDocument.mockResolvedValue({ description: '自身に [S:strength] [N:1] を付与。' })
    const view = render(
      <TooltipHost>
        <ContentWorkbenchScreen initialKind="cards" onClose={() => {}} />
      </TooltipHost>,
    )

    await waitFor(() => expect(view.container.querySelector('.content-preview-rich-text [data-term="strength"]')).not.toBeNull())
    fireEvent.mouseEnter(view.container.querySelector('.content-preview-rich-text [data-term="strength"]')!)

    expect(await screen.findByRole('tooltip')).toHaveTextContent('筋力')
    expect(screen.getByRole('tooltip')).toHaveTextContent('アタックで与えるダメージがX増加する。')
  })

  it('publishes the current active version as a clean standard v1 after confirmation', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    render(<ContentWorkbenchScreen initialKind="cards" onClose={() => {}} />)

    await screen.findByLabelText('表示名')
    fireEvent.click(screen.getByRole('button', { name: '決定稿を標準v1へ' }))

    await waitFor(() => expect(api.saveContentAsStandardV1).toHaveBeenCalledWith(
      'cards', 'card_a', expect.objectContaining({ activeVersion: 'v1' }), 'r1',
    ))
    expect(await screen.findByText('使用中バージョンを標準データの v1 として整理し、実ゲームへ反映しました。')).toBeTruthy()
    confirm.mockRestore()
  })

  it('adds explicit pool defaults when cloning a legacy card', async () => {
    const prompt = vi.spyOn(window, 'prompt').mockReturnValue('new_reward_card')
    render(<ContentWorkbenchScreen initialKind="cards" onClose={() => {}} />)

    await screen.findByLabelText('表示名')
    fireEvent.click(screen.getByRole('button', { name: '複製して新規' }))

    await waitFor(() => expect(api.saveContentDocument).toHaveBeenCalledWith(
      'cards',
      'new_reward_card',
      expect.objectContaining({
        id: 'new_reward_card',
        poolKinds: ['Reward'],
        poolClass: 'Striker',
        eligibleCharacterIds: [],
      }),
    ))
    prompt.mockRestore()
  })

  it('filters cards by pool membership and combines the filter with search', async () => {
    const view = render(<ContentWorkbenchScreen initialKind="cards" onClose={() => {}} />)

    const classFilter = await screen.findByLabelText('所属クラスで絞り込み')
    await waitFor(() => expect(view.container.querySelector('[data-content-id="card_a"]')).toBeInTheDocument())
    fireEvent.change(classFilter, { target: { value: 'Striker' } })
    expect(view.container.querySelector('[data-content-id="card_a"]')).toBeInTheDocument()
    expect(view.container.querySelector('[data-content-id="card_b"]')).not.toBeInTheDocument()

    fireEvent.change(screen.getByPlaceholderText('名前 / IDを検索'), { target: { value: 'card_b' } })
    expect(screen.getByText('該当するデータはありません。')).toBeInTheDocument()

    fireEvent.change(screen.getByPlaceholderText('名前 / IDを検索'), { target: { value: '' } })
    fireEvent.change(classFilter, { target: { value: 'unpooled' } })
    expect(view.container.querySelector('[data-content-id="card_c"]')).toBeInTheDocument()
    expect(view.container.querySelector('[data-content-id="card_a"]')).not.toBeInTheDocument()
  })

  it('uses characterClass for characters and resets the filter when changing kind', async () => {
    const mixedMeta = {
      ...meta,
      kinds: [...meta.kinds, {
        id: 'characters', label: 'キャラクター', group: 'プレイヤー', sourceDirectory: 'Characters',
        overrideDirectory: 'characters', canCreate: true, canDelete: true,
        supportsGamePreview: true, limitation: null,
      }],
    }
    const characterItems = [
      { id: 'striker', name: '剣士', hasBase: true, hasOverride: false, revision: 'r1', characterClass: 'Striker' },
      { id: 'guardian', name: '騎士', hasBase: true, hasOverride: false, revision: 'r1', characterClass: 'Guardian' },
    ]
    api.fetchContentMeta.mockResolvedValue(mixedMeta)
    api.fetchContentList.mockImplementation((contentKind: string) => Promise.resolve(
      contentKind === 'characters' ? characterItems : items,
    ))
    api.fetchContentDocument.mockImplementation((contentKind: string, id: string) => Promise.resolve(
      contentKind === 'characters'
        ? {
          kind: 'characters', id, name: id,
          document: { id, name: id, characterClass: id === 'guardian' ? 'Guardian' : 'Striker', deck: [] },
          hasBase: true, hasOverride: false, revision: 'r1', referencedBy: [],
        }
        : documentFor(id),
    ))

    const view = render(<ContentWorkbenchScreen initialKind="cards" onClose={() => {}} />)
    const cardClassFilter = await screen.findByLabelText('所属クラスで絞り込み')
    await waitFor(() => expect(view.container.querySelector('[data-content-id="card_a"]')).toBeInTheDocument())
    fireEvent.change(cardClassFilter, { target: { value: 'Striker' } })
    expect(cardClassFilter).toHaveValue('Striker')

    fireEvent.click(screen.getByRole('button', { name: 'キャラクター' }))
    const characterClassFilter = await screen.findByLabelText('所属クラスで絞り込み')
    expect(characterClassFilter).toHaveValue('all')
    expect(screen.queryByRole('option', { name: 'ユニバーサル' })).not.toBeInTheDocument()
    expect(screen.queryByRole('option', { name: 'プール外' })).not.toBeInTheDocument()

    fireEvent.change(characterClassFilter, { target: { value: 'Guardian' } })
    await waitFor(() => expect(view.container.querySelector('[data-content-id="guardian"]')).toBeInTheDocument())
    expect(view.container.querySelector('[data-content-id="striker"]')).not.toBeInTheDocument()
  })

  it('previews character turn resources and the structured trait', async () => {
    const characterMeta = {
      ...meta,
      kinds: [{
        id: 'characters', label: 'キャラクター', group: 'プレイヤー', sourceDirectory: 'Characters',
        overrideDirectory: 'characters', canCreate: true, canDelete: true,
        supportsGamePreview: true, limitation: null,
      }],
      enums: {
        ...meta.enums,
        optionLabels: {
          ...meta.enums.optionLabels,
          'characterTraitTriggers:OnComboReached': '最大コンボ達成時',
        },
      },
    }
    const character = {
      kind: 'characters', id: 'default', name: '見習い冒険者',
      document: {
        id: 'default', name: '見習い冒険者', characterClass: 'Striker', maxHp: 80,
        startingGold: 99, potionSlotCount: 3, energyPerTurn: 3, drawPerTurn: 5,
        deck: ['basic_attack'],
        trait: {
          id: 'chain_flash', name: '連閃', trigger: 'OnComboReached', threshold: 4,
          usesPerTurn: 1, description: '最大コンボ4で残光を得る。', effects: [],
        },
      },
      hasBase: true, hasOverride: false, revision: 'r1', referencedBy: [],
    }
    api.fetchContentMeta.mockResolvedValue(characterMeta)
    api.fetchContentList.mockResolvedValue([{
      id: 'default', name: '見習い冒険者', hasBase: true, hasOverride: false, revision: 'r1',
    }])
    api.fetchContentDocument.mockResolvedValue(character)
    api.previewContentDocument.mockResolvedValue({
      description: '刃風展開\n1枚引く。',
      classification: 'クラス: ストライカー',
      initialDeck: ['basic_attack'],
      energyPerTurn: 3,
      drawPerTurn: 5,
      traitName: '連閃',
      traitDescription: '最大コンボ4で残光を得る。',
      traitTrigger: '最大コンボ達成時',
      traitEffects: '自身に [S:afterimage] [N:1] を付与。',
    })

    render(<ContentWorkbenchScreen initialKind="characters" onClose={() => {}} />)

    expect(await screen.findByText('ターン開始時: エナジー 3 / ドロー 5枚')).toBeTruthy()
    expect(await screen.findByText('特性: 連閃')).toBeTruthy()
    expect(screen.getByText('発動: 最大コンボ達成時')).toBeTruthy()
    expect(screen.getAllByText('最大コンボ4で残光を得る。')).toHaveLength(2)
  })
})
