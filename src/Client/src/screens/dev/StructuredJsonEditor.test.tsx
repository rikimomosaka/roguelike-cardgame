import { fireEvent, render, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { DevContentEnums } from '../../api/devContent'
import { StructuredJsonEditor } from './StructuredJsonEditor'

const meta: DevContentEnums = {
  cardTypes: ['Attack', 'Skill', 'Power'],
  characterClasses: ['Striker', 'Guardian', 'Tactician', 'Enhancer'],
  cardPoolClasses: ['Universal', 'Striker', 'Guardian', 'Tactician', 'Enhancer'],
  cardPoolKinds: ['Basic', 'Reward'],
  cardAbilities: ['finisher', 'reprise', 'contingency'],
  contingencyTriggerTimings: ['TurnStart', 'BeforeEnemyAttack', 'ComboReached'],
  characterTraitTriggers: [
    'OnTurnStart', 'OnComboReached', 'OnContingencyTriggered', 'OnResonanceConsumed',
  ],
  rarities: [0, 1, 2, 3, 4, 5],
  scopes: ['self', 'single', 'random', 'all'],
  sides: ['enemy', 'ally'],
  selects: ['random', 'choose', 'all'],
  piles: ['hand', 'draw', 'discard', 'exhaust'],
  tiers: ['Weak', 'Strong', 'Elite', 'Boss'],
  triggers: ['OnPlayCard', 'Passive'],
  actions: [
    'attack', 'block', 'buff', 'debuff', 'draw', 'gainEnergy', 'gainComboGauge',
    'gainMaxEnergy', 'recoverFromDiscard', 'modifyCardCost', 'unknownTileWeightDelta',
  ],
  amountSources: ['handCount', 'maxComboThisTurn', 'currentCost'],
  eventEffectTypes: ['heal', 'gainGold', 'gainCard'],
  eventConditionTypes: ['minGold', 'minHp'],
  eventRarities: ['common', 'uncommon', 'rare'],
  tileKinds: ['Enemy', 'Rest', 'Event'],
  optionLabels: {
    'actions:attack': '攻撃', 'actions:block': 'ブロック', 'actions:buff': 'バフ', 'actions:debuff': 'デバフ',
    'actions:draw': 'ドロー', 'actions:gainEnergy': 'エナジー獲得',
    'actions:gainComboGauge': 'コンボゲージ獲得',
    'actions:gainMaxEnergy': 'エナジー回復量増加', 'actions:modifyCardCost': 'カードコスト変更',
    'actions:recoverFromDiscard': '捨札回収',
    'actions:unknownTileWeightDelta': '不明マス重み補正',
    'cardTypes:Attack': '攻撃', 'cardTypes:Skill': 'スキル', 'cardTypes:Power': 'パワー',
    'characterClasses:Striker': 'ストライカー', 'characterClasses:Guardian': 'ガーディアン',
    'cardPoolClasses:Universal': 'ユニバーサル', 'cardPoolClasses:Striker': 'ストライカー',
    'cardPoolKinds:Basic': '基本カード', 'cardPoolKinds:Reward': '報酬カード',
    'cardAbilities:finisher': '終撃', 'cardAbilities:reprise': '再演',
    'cardAbilities:contingency': '伏策',
    'contingencyTriggers:TurnStart': 'ターン開始時',
    'contingencyTriggers:BeforeEnemyAttack': '相手のアタック時',
    'contingencyTriggers:ComboReached': 'コンボ達成時',
    'characterTraitTriggers:OnTurnStart': 'ターン開始時',
    'characterTraitTriggers:OnComboReached': '最大コンボ達成時',
    'characterTraitTriggers:OnContingencyTriggered': '伏策発動後',
    'characterTraitTriggers:OnResonanceConsumed': '共鳴発動後',
    'eventEffectTypes:gainCard': '指定カード獲得',
    'rarities:0': '基本', 'rarities:1': 'コモン', 'scopes:self': '自身', 'scopes:single': '単体',
    'scopes:random': 'ランダム', 'scopes:all': '全体', 'sides:enemy': '敵',
    'sides:ally': '味方', 'statuses:strength': '筋力', 'statuses:weak': '脱力',
    'selects:random': 'ランダム', 'selects:choose': '選択', 'selects:all': '全て',
    'piles:hand': '手札', 'piles:draw': '山札', 'piles:discard': '捨札', 'piles:exhaust': '除外',
    'amountSources:handCount': '手札枚数',
    'amountSources:maxComboThisTurn': 'このターンの最大コンボ',
    'amountSources:currentCost': '対象カードの現在コスト',
  },
  statuses: [
    { id: 'strength', name: '筋力', description: '説明', kind: 'Buff', isPermanent: true, tickDirection: 'None' },
    { id: 'afterimage', name: '残光', description: '説明', kind: 'Buff', isPermanent: false, tickDirection: 'None' },
    { id: 'weak', name: '脱力', description: '説明', kind: 'Debuff', isPermanent: false, tickDirection: 'Down' },
  ],
  keywords: [{ id: 'wild', name: 'ワイルド', description: 'combo' }],
}

describe('StructuredJsonEditor', () => {
  it('adds an event effect template rather than a battle CardEffect', () => {
    const onChange = vi.fn()
    const { container } = render(
      <StructuredJsonEditor
        value={{ choices: [{ label: '選択', effects: [], resultMessage: '' }] }}
        onChange={onChange}
        meta={meta}
        references={{}}
      />,
    )

    const add = container.querySelector<HTMLButtonElement>(
      '[data-path="$.choices[0].effects"] > .content-json-add',
    )
    expect(add).not.toBeNull()
    fireEvent.click(add!)

    expect(onChange).toHaveBeenCalledWith({
      choices: [{ label: '選択', effects: [{ type: 'heal', amount: 1 }], resultMessage: '' }],
    })
  })

  it('replaces event effect inputs with a labeled card selector for gainCard', () => {
    const onChange = vi.fn()
    const view = render(
      <StructuredJsonEditor
        value={{ choices: [{ label: '選択', effects: [{ type: 'heal', amount: 5 }], resultMessage: '' }] }}
        onChange={onChange}
        meta={meta}
        references={{ cards: [{
          id: 'strike', name: 'ストライク', hasBase: true, hasOverride: false, revision: 'r1',
        }] }}
        contentKind="events"
      />,
    )

    fireEvent.change(view.getByLabelText('イベント処理'), { target: { value: 'gainCard' } })
    expect(onChange).toHaveBeenCalledWith({
      choices: [{
        label: '選択',
        effects: [{ type: 'gainCard', cardId: 'strike' }],
        resultMessage: '',
      }],
    })
  })

  it('preserves unknown sibling fields when a known value changes', () => {
    const onChange = vi.fn()
    const { getByDisplayValue } = render(
      <StructuredJsonEditor
        value={{ name: '旧名', futureExtension: { enabled: true } }}
        onChange={onChange}
        meta={meta}
        references={{}}
      />,
    )

    fireEvent.change(getByDisplayValue('旧名'), { target: { value: '新名' } })

    expect(onChange).toHaveBeenCalledWith({
      name: '新名',
      futureExtension: { enabled: true },
    })
  })

  it('creates upgraded card effects by cloning the normal effects', () => {
    const onChange = vi.fn()
    const value = {
      activeVersion: 'v1',
      versions: [{
        version: 'v1',
        spec: { effects: [{ action: 'attack', scope: 'single', amount: 5 }] },
      }],
    }
    const { getByRole } = render(
      <StructuredJsonEditor
        value={value}
        onChange={onChange}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )

    fireEvent.click(getByRole('button', { name: '＋ 通常効果を複製して強化後を作成' }))

    expect(onChange).toHaveBeenCalledWith({
      activeVersion: 'v1',
      versions: [{
        version: 'v1',
        spec: {
          effects: [{ action: 'attack', scope: 'single', amount: 5 }],
          upgradedEffects: [{ action: 'attack', scope: 'single', amount: 5 }],
        },
      }],
    })
  })

  it('explains null as an unset optional value before selecting its input format', () => {
    const onChange = vi.fn()
    const { getByText, getByRole } = render(
      <StructuredJsonEditor value={{ label: null }} onChange={onChange} meta={meta} references={{}} />,
    )

    expect(getByText('未設定（null）')).toBeTruthy()
    expect(getByText('値を設定する場合は、入力形式を選んでください。')).toBeTruthy()
    fireEvent.click(getByRole('button', { name: '文字を設定' }))
    expect(onChange).toHaveBeenCalledWith({ label: '' })
  })

  it('shows stable IDs together with Japanese names in selection controls', () => {
    const { getByLabelText, getByRole } = render(
      <StructuredJsonEditor
        value={{ action: 'attack', scope: 'single', amount: 5 }}
        onChange={() => {}}
        meta={meta}
        references={{}}
      />,
    )

    expect(getByLabelText('効果種別')).toHaveDisplayValue('attack(攻撃)')
    expect(getByRole('option', { name: 'block(ブロック)' })).toBeTruthy()
    expect(getByLabelText('範囲')).toHaveDisplayValue('single(単体)')
  })

  it('adds the complete action-specific inputs when the effect action changes', () => {
    const onChange = vi.fn()
    const value = { action: 'attack', scope: 'single', side: 'enemy', amount: 5 }
    const view = render(
      <StructuredJsonEditor
        value={value}
        onChange={onChange}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )

    fireEvent.change(view.getByLabelText('効果種別'), { target: { value: 'debuff' } })
    const debuff = {
      action: 'debuff',
      scope: 'single',
      side: 'enemy',
      statusChanges: [{ name: 'weak', amount: 1 }],
    }
    expect(onChange).toHaveBeenCalledWith(debuff)

    view.rerender(
      <StructuredJsonEditor
        value={debuff}
        onChange={onChange}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )
    expect(view.getByLabelText('ステータス')).toHaveDisplayValue('weak(脱力)')
    expect(view.getByLabelText('数値')).toHaveValue(1)
    expect(view.getByLabelText('対象側')).toHaveDisplayValue('enemy(敵)')
  })

  it('opens only the active version initially and lets older versions expand on demand', () => {
    const value = {
      id: 'versioned_card',
      activeVersion: 'v3',
      versions: [
        { version: 'v1', label: 'old', spec: { description: '旧版1' } },
        { version: 'v2', label: 'old', spec: { description: '旧版2' } },
        { version: 'v3', label: 'active', spec: { description: '使用中' } },
      ],
    }
    const view = render(
      <StructuredJsonEditor value={value} onChange={() => {}} meta={meta} references={{}} contentKind="cards" />,
    )

    expect(view.getByRole('button', { name: 'v1 old' })).toHaveAttribute('aria-expanded', 'false')
    expect(view.getByRole('button', { name: 'v2 old' })).toHaveAttribute('aria-expanded', 'false')
    expect(view.getByRole('button', { name: 'v3（使用中） active' })).toHaveAttribute('aria-expanded', 'true')
    expect(view.queryByDisplayValue('旧版1')).toBeNull()
    expect(view.getByDisplayValue('使用中')).toBeTruthy()

    fireEvent.click(view.getByRole('button', { name: 'v1 old' }))
    expect(view.getByDisplayValue('旧版1')).toBeTruthy()
  })

  it('creates a complete editable card spec when the first version is added', () => {
    const onChange = vi.fn()
    const value = { id: 'new_card', activeVersion: 'v1', versions: [] }
    const view = render(
      <StructuredJsonEditor value={value} onChange={onChange} meta={meta} references={{}} contentKind="cards" />,
    )

    fireEvent.click(view.getByRole('button', { name: '＋ バージョンを追加' }))

    expect(onChange).toHaveBeenCalledWith({
      ...value,
      versions: [{
        version: 'v1',
        spec: {
          rarity: 1,
          cardType: 'Skill',
          cost: 1,
          effects: [],
          upgradedEffects: [],
          keywords: [],
          upgradedKeywords: [],
        },
      }],
    })
  })

  it('clones the active spec and allocates the next version number', () => {
    const onChange = vi.fn()
    const spec = { rarity: 2, cardType: 'Attack', cost: 3, effects: [] }
    const value = {
      id: 'existing_card', activeVersion: 'v2',
      versions: [{ version: 'v2', spec }],
    }
    const view = render(
      <StructuredJsonEditor value={value} onChange={onChange} meta={meta} references={{}} contentKind="cards" />,
    )

    fireEvent.click(view.getByRole('button', { name: '＋ バージョンを追加' }))

    expect(onChange).toHaveBeenCalledWith({
      ...value,
      versions: [...value.versions, { version: 'v3', spec }],
    })
  })

  it('creates independent normal and upgraded keyword fields without copying either side', () => {
    const onChange = vi.fn()
    const value = { rarity: 1, cardType: 'Attack', cost: 1, effects: [] }
    const { getByRole, rerender } = render(
      <StructuredJsonEditor value={value} onChange={onChange} meta={meta} references={{}} contentKind="cards" />,
    )

    fireEvent.click(getByRole('button', { name: '＋ 通常版キーワード欄を追加' }))
    expect(onChange).toHaveBeenCalledWith({ ...value, keywords: [] })
    fireEvent.click(getByRole('button', { name: '＋ 強化後キーワード欄を追加' }))
    expect(onChange).toHaveBeenCalledWith({ ...value, upgradedKeywords: [] })

    rerender(
      <StructuredJsonEditor
        value={{ ...value, keywords: ['wild'] }}
        onChange={onChange}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )
    expect(getByRole('option', { name: 'wild(ワイルド)' })).toBeTruthy()
  })

  it('adds known optional fields without asking for a raw JSON property name', () => {
    const onChange = vi.fn()
    const value = { rarity: 1, cardType: 'Attack', cost: 2, effects: [] }
    const { getByLabelText } = render(
      <StructuredJsonEditor value={value} onChange={onChange} meta={meta} references={{}} contentKind="cards" />,
    )

    fireEvent.change(getByLabelText('追加できる既知の設定'), { target: { value: 'upgradedCost' } })
    expect(onChange).toHaveBeenCalledWith({ ...value, upgradedCost: 2 })
  })

  it('authors typed card abilities from schema-backed choices', () => {
    const onChange = vi.fn()
    const value = { rarity: 1, cardType: 'Skill', cost: 1, effects: [], abilities: [] }
    const view = render(
      <StructuredJsonEditor value={value} onChange={onChange} meta={meta} references={{}} contentKind="cards" />,
    )

    const add = view.container.querySelector<HTMLButtonElement>(
      '[data-path="$.abilities"] > .content-json-add',
    )
    expect(add).not.toBeNull()
    fireEvent.click(add!)
    expect(onChange).toHaveBeenCalledWith({ ...value, abilities: [{ type: 'finisher' }] })

    view.rerender(
      <StructuredJsonEditor
        value={{ ...value, abilities: [{ type: 'contingency', trigger: { timing: 'ComboReached', comboThreshold: 3 } }] }}
        onChange={onChange}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )
    expect(view.getByRole('option', { name: 'reprise(再演)' })).toBeTruthy()
    expect(view.getByRole('option', { name: 'ComboReached(コンボ達成時)' })).toBeTruthy()
  })

  it('prevents duplicate and incompatible typed abilities', () => {
    const onChange = vi.fn()
    const value = {
      rarity: 1, cardType: 'Skill', cost: 1, effects: [],
      abilities: [{ type: 'finisher' }],
    }
    const view = render(
      <StructuredJsonEditor
        value={value}
        onChange={onChange}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )

    const add = view.container.querySelector<HTMLButtonElement>(
      '[data-path="$.abilities"] > .content-json-add',
    )
    fireEvent.click(add!)
    expect(onChange).toHaveBeenCalledWith({
      ...value,
      abilities: [{
        type: 'finisher',
      }, {
        type: 'reprise', filter: { cardTypes: ['Skill'], maxOriginalCost: 3 },
      }],
    })

    view.rerender(
      <StructuredJsonEditor
        value={{ ...value, abilities: [{ type: 'finisher' }, { type: 'reprise', filter: {} }] }}
        onChange={onChange}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )
    expect(add).toBeDisabled()

    const typeSelectors = view.getAllByLabelText('イベント処理')
    expect(within(typeSelectors[0]).queryByRole('option', { name: 'reprise(再演)' })).toBeNull()
    expect(within(typeSelectors[0]).queryByRole('option', { name: 'contingency(伏策)' })).toBeNull()
  })

  it('keeps typed abilities limited to Attack and Skill cards', () => {
    const power = render(
      <StructuredJsonEditor
        value={{ rarity: 1, cardType: 'Power', cost: 1, effects: [], abilities: [] }}
        onChange={() => {}}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )
    const add = power.container.querySelector<HTMLButtonElement>(
      '[data-path="$.abilities"] > .content-json-add',
    )
    expect(add).toBeDisabled()
    expect(add).toHaveAttribute('title', '固有能力はアタックまたはスキルにのみ設定できます。')
    power.unmount()

    const configured = render(
      <StructuredJsonEditor
        value={{
          rarity: 1, cardType: 'Attack', cost: 1, effects: [],
          abilities: [{ type: 'finisher' }],
        }}
        onChange={() => {}}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )
    expect(configured.queryByRole('option', { name: 'Power(パワー)' })).toBeNull()
  })

  it('offers usage limits on the card itself and not on an individual effect', () => {
    const value = { rarity: 1, cardType: 'Skill', cost: 1, effects: [] }
    const root = render(
      <StructuredJsonEditor value={value} onChange={() => {}} meta={meta} references={{}} contentKind="cards" />,
    )
    expect(root.getByRole('option', { name: 'usesPerTurn(本体の1ターン使用回数)' })).toBeTruthy()
    expect(root.getByRole('option', { name: 'upgradedUsesPerTurn(強化版本体の1ターン使用回数)' })).toBeTruthy()
    root.unmount()

    const effect = render(
      <StructuredJsonEditor
        value={{ action: 'draw', scope: 'self', amount: 1 }}
        onChange={() => {}}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )
    expect(effect.queryByRole('option', { name: /usesPerTurn/ })).toBeNull()
  })

  it.each(['draw', 'gainEnergy', 'gainMaxEnergy'])(
    'allows %s to target another allied participant',
    (action) => {
      const onChange = vi.fn()
      const view = render(
        <StructuredJsonEditor
          value={{ action, scope: 'self', amount: 1 }}
          onChange={onChange}
          meta={meta}
          references={{}}
          contentKind="cards"
        />,
      )

      fireEvent.change(view.getByLabelText('範囲'), { target: { value: 'all' } })
      expect(onChange).toHaveBeenCalledWith({
        action,
        scope: 'all',
        side: 'ally',
        amount: 1,
      })
    },
  )

  it('changes non-self card-cost selection from choose to all', () => {
    const onChange = vi.fn()
    const view = render(
      <StructuredJsonEditor
        value={{
          action: 'modifyCardCost', scope: 'self', amount: -1,
          pile: 'hand', select: 'choose', cardCount: 1,
          costDuration: 'untilPlayed',
        }}
        onChange={onChange}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )

    fireEvent.change(view.getByLabelText('範囲'), { target: { value: 'single' } })
    expect(onChange).toHaveBeenCalledWith({
      action: 'modifyCardCost', scope: 'single', side: 'ally', amount: -1,
      pile: 'hand', select: 'all', costDuration: 'untilPlayed',
    })
  })

  it('offers every card-controlled unit setting without reintroducing lifetime', () => {
    const onChange = vi.fn()
    const value = { id: 'new_unit', name: '新召喚', imageId: 'new_unit', hp: 20 }
    const view = render(
      <StructuredJsonEditor
        value={value}
        onChange={onChange}
        meta={meta}
        references={{}}
        contentKind="units"
      />,
    )

    const addable = view.getByLabelText('追加できる既知の設定')
    expect(view.getByRole('option', { name: 'deck(初期デッキ)' })).toBeTruthy()
    expect(view.getByRole('option', { name: 'initialEnergy(初期エナジー)' })).toBeTruthy()
    expect(view.getByRole('option', { name: 'energyPerTurn(ターン開始時エナジー回復量)' })).toBeTruthy()
    expect(view.getByRole('option', { name: 'aiProfileId(CPU行動方針)' })).toBeTruthy()
    expect(view.queryByRole('option', { name: /lifetimeTurns/ })).toBeNull()

    fireEvent.change(addable, { target: { value: 'deck' } })
    expect(onChange).toHaveBeenCalledWith({ ...value, deck: [] })
  })

  it('edits card pool metadata with labeled choices and character references', () => {
    const onChange = vi.fn()
    const value = {
      id: 'new_card', name: '新カード', poolKinds: ['Reward'], poolClass: 'Striker',
      eligibleCharacterIds: ['default'], activeVersion: 'v1', versions: [],
    }
    const view = render(
      <StructuredJsonEditor
        value={value}
        onChange={onChange}
        meta={meta}
        references={{ characters: [{
          id: 'default', name: '見習い冒険者', hasBase: true, hasOverride: false, revision: 'r1',
        }] }}
        contentKind="cards"
      />,
    )

    expect(view.getByLabelText('所属カードプール')).toHaveDisplayValue('Striker(ストライカー)')
    expect(view.getByDisplayValue('Reward(報酬カード)')).toBeTruthy()
    expect(view.getByDisplayValue('default(見習い冒険者)')).toBeTruthy()
  })

  it('removes the card acquisition group as an explicit empty pool', () => {
    const onChange = vi.fn()
    const value = {
      id: 'new_card', name: 'new card', poolKinds: ['Reward'], poolClass: 'Striker',
      eligibleCharacterIds: ['default'], activeVersion: 'v1', versions: [],
    }
    const view = render(
      <StructuredJsonEditor
        value={value}
        onChange={onChange}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )

    fireEvent.click(view.getByRole('button', { name: /poolKinds/ }))

    expect(onChange).toHaveBeenCalledWith({
      id: 'new_card',
      name: 'new card',
      poolKinds: [],
      activeVersion: 'v1',
      versions: [],
    })
  })

  it('offers character class and card pool fields as known root settings', () => {
    const card = render(
      <StructuredJsonEditor
        value={{ id: 'card', name: 'カード', activeVersion: 'v1', versions: [] }}
        onChange={() => {}}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )
    expect(card.getByRole('option', { name: 'poolKinds(用途)' })).toBeTruthy()
    expect(card.getByRole('option', { name: 'poolClass(所属カードプール)' })).toBeTruthy()
    expect(card.getByRole('option', { name: 'eligibleCharacterIds(取得可能キャラクター)' })).toBeTruthy()
    card.unmount()

    const character = render(
      <StructuredJsonEditor
        value={{ id: 'hero', name: '冒険者', maxHp: 80, deck: ['strike'] }}
        onChange={() => {}}
        meta={meta}
        references={{}}
        contentKind="characters"
      />,
    )
    expect(character.getByRole('option', { name: 'characterClass(キャラクタークラス)' })).toBeTruthy()
    expect(character.getByRole('option', { name: 'energyPerTurn(ターン開始時エナジー回復量)' })).toBeTruthy()
    expect(character.getByRole('option', { name: 'drawPerTurn(ターン開始時ドロー枚数)' })).toBeTruthy()
    expect(character.getByRole('option', { name: 'trait(特性)' })).toBeTruthy()
  })

  it('edits a character trait with its own trigger choices and conditional threshold', () => {
    const onChange = vi.fn()
    const value = {
      id: 'hero', name: '冒険者', maxHp: 80, deck: ['strike'],
      trait: {
        id: 'combo_trait', name: '連閃', trigger: 'OnComboReached', threshold: 4,
        usesPerTurn: 1, description: '最大コンボで発動する。', effects: [],
      },
    }
    const view = render(
      <StructuredJsonEditor
        value={value}
        onChange={onChange}
        meta={meta}
        references={{}}
        contentKind="characters"
      />,
    )

    expect(view.getByLabelText('発動条件')).toHaveDisplayValue('OnComboReached(最大コンボ達成時)')
    expect(view.getByLabelText('発動しきい値')).toHaveValue(4)
    expect(view.getByLabelText('特性の1ターン発動回数（0は無制限）')).toHaveValue(1)

    fireEvent.change(view.getByLabelText('発動条件'), { target: { value: 'OnTurnStart' } })
    expect(onChange).toHaveBeenCalledWith({
      ...value,
      trait: {
        id: 'combo_trait', name: '連閃', trigger: 'OnTurnStart', usesPerTurn: 1,
        description: '最大コンボで発動する。', effects: [],
      },
    })
  })

  it('uses the canonical status name even when the duplicated option label is absent', () => {
    const view = render(
      <StructuredJsonEditor
        value={{
          action: 'buff', scope: 'self',
          statusChanges: [{ name: 'afterimage', amount: 1 }],
        }}
        onChange={() => {}}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )

    expect(view.getByLabelText('ステータス')).toHaveDisplayValue('afterimage(残光)')
    expect(view.queryByText(/表示名未登録/)).toBeNull()
  })

  it('builds all required fields for gainComboGauge', () => {
    const onChange = vi.fn()
    const view = render(
      <StructuredJsonEditor
        value={{ action: 'attack', scope: 'single', side: 'enemy', amount: 5 }}
        onChange={onChange}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )

    fireEvent.change(view.getByLabelText('効果種別'), { target: { value: 'gainComboGauge' } })
    expect(onChange).toHaveBeenCalledWith({
      action: 'gainComboGauge', scope: 'self', amount: 5,
    })
  })

  it('uses currentCost only for modifyCardCost and normalizes its amount', () => {
    const onChange = vi.fn()
    const effect = {
      action: 'modifyCardCost', scope: 'self', amount: -1, amountSource: null,
      pile: 'hand', select: 'all', costDuration: 'untilPlayed',
    }
    const view = render(
      <StructuredJsonEditor
        value={effect}
        onChange={onChange}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )

    fireEvent.change(view.getByLabelText('参照する変数'), { target: { value: 'currentCost' } })
    expect(onChange).toHaveBeenCalledWith({
      ...effect, amount: 0, amountSource: 'currentCost',
    })

    view.rerender(
      <StructuredJsonEditor
        value={{ action: 'attack', scope: 'single', side: 'enemy', amount: 5, amountSource: null }}
        onChange={onChange}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )
    expect(view.queryByRole('option', { name: 'currentCost(対象カードの現在コスト)' })).toBeNull()
  })

  it('drops currentCost when changing to a different action', () => {
    const onChange = vi.fn()
    const view = render(
      <StructuredJsonEditor
        value={{
          action: 'modifyCardCost', scope: 'self', amount: 0, amountSource: 'currentCost',
          pile: 'hand', select: 'all', costDuration: 'untilPlayed',
        }}
        onChange={onChange}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )

    fireEvent.change(view.getByLabelText('効果種別'), { target: { value: 'draw' } })
    expect(onChange).toHaveBeenCalledWith({ action: 'draw', scope: 'self', amount: 1 })
  })

  it('restores a valid literal when currentCost is removed directly', () => {
    const onChange = vi.fn()
    const value = {
      action: 'modifyCardCost', scope: 'self', amount: 0, amountSource: 'currentCost',
      pile: 'hand', select: 'all', costDuration: 'untilPlayed',
    }
    const view = render(
      <StructuredJsonEditor
        value={value}
        onChange={onChange}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )

    fireEvent.click(view.getByRole('button', { name: 'amountSourceを削除' }))
    expect(onChange).toHaveBeenCalledWith({
      action: 'modifyCardCost', scope: 'self', amount: -1,
      pile: 'hand', select: 'all', costDuration: 'untilPlayed',
    })
  })

  it('coerces choose to all when a card-cost modifier gains a trigger', () => {
    const onChange = vi.fn()
    const value = {
      action: 'modifyCardCost', scope: 'self', amount: -1,
      pile: 'hand', select: 'choose', cardCount: 1,
      costDuration: 'untilPlayed', trigger: null,
    }
    const view = render(
      <StructuredJsonEditor
        value={value}
        onChange={onChange}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )

    fireEvent.change(view.getByLabelText('発動条件'), { target: { value: 'OnPlayCard' } })
    expect(onChange).toHaveBeenCalledWith({
      action: 'modifyCardCost', scope: 'self', amount: -1,
      pile: 'hand', select: 'all', costDuration: 'untilPlayed', trigger: 'OnPlayCard',
    })

    view.rerender(
      <StructuredJsonEditor
        value={{ ...value, select: 'all', trigger: 'OnPlayCard' }}
        onChange={onChange}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )
    expect(view.queryByRole('option', { name: 'choose(選択)' })).toBeNull()
  })

  it('restricts piles to destinations supported by the selected action', () => {
    const view = render(
      <StructuredJsonEditor
        value={{
          action: 'recoverFromDiscard', scope: 'self', amount: 1,
          pile: 'hand', select: 'choose',
        }}
        onChange={() => {}}
        meta={meta}
        references={{}}
        contentKind="cards"
      />,
    )

    const pile = view.getByLabelText('対象領域')
    expect(within(pile).getByRole('option', { name: 'hand(手札)' })).toBeTruthy()
    expect(within(pile).getByRole('option', { name: 'exhaust(除外)' })).toBeTruthy()
    expect(within(pile).queryByRole('option', { name: 'draw(山札)' })).toBeNull()
    expect(within(pile).queryByRole('option', { name: 'discard(捨札)' })).toBeNull()
  })
})
