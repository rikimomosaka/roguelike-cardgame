// Phase 10.5.M: 構造化フォームで扱う CardSpec / CardEffect の TS 型 + 補助関数。
//
// CardSpec の JSON shape は server/Core が期待するものと一致させる。
// null フィールドは specToJson で省略する (CardJsonLoader 互換のため)。

export type StatusChange = {
  name: string
  amount: number
  extraFields?: Record<string, unknown>
}

export type RepriseCardFilter = {
  cardTypes: string[] | null
  minOriginalCost: number | null
  maxOriginalCost: number | null
  minPaidCost: number | null
  maxPaidCost: number | null
  requiredKeywords: string[] | null
  extraFields?: Record<string, unknown>
}

export type CardAbility =
  | { type: 'finisher'; extraFields?: Record<string, unknown> }
  | { type: 'reprise'; filter: RepriseCardFilter; extraFields?: Record<string, unknown> }
  | {
      type: 'contingency'
      trigger: {
        timing: 'TurnStart' | 'BeforeEnemyAttack' | 'ComboReached'
        comboThreshold: number | null
        extraFields?: Record<string, unknown>
      }
      extraFields?: Record<string, unknown>
    }

export type CardEffect = {
  action: string
  scope: string | null
  side: string | null
  amount: number
  name: string | null
  unitId: string | null
  comboMin: number | null
  maxComboMin: number | null
  pile: string | null
  battleOnly: boolean
  cardRefId: string | null
  select: string | null
  amountSource: string | null
  trigger: string | null
  chancePercent: number | null
  costDuration: string | null
  cardCount: number | null
  statusChanges: StatusChange[] | null
  extraFields?: Record<string, unknown>
}

export type CardSpec = {
  rarity: number
  cardType: string
  cost: number | null
  upgradedCost: number | null
  comboGaugeCost: number | null
  effects: CardEffect[]
  upgradedEffects: CardEffect[] | null
  description: string | null
  upgradedDescription: string | null
  keywords: string[] | null
  upgradedKeywords: string[] | null
  usesPerTurn: number | null
  upgradedUsesPerTurn: number | null
  abilities: CardAbility[] | null
  upgradedAbilities: CardAbility[] | null
  extraFields?: Record<string, unknown>
}

const CARD_EFFECT_FIELDS = new Set([
  'action', 'scope', 'side', 'amount', 'name', 'unitId', 'comboMin', 'maxComboMin',
  'pile', 'battleOnly', 'cardRefId', 'select', 'amountSource', 'trigger',
  'chancePercent', 'costDuration', 'cardCount', 'statusChanges',
])

const CARD_SPEC_FIELDS = new Set([
  'rarity', 'cardType', 'cost', 'upgradedCost', 'comboGaugeCost', 'effects',
  'upgradedEffects', 'description', 'upgradedDescription', 'keywords',
  'upgradedKeywords', 'usesPerTurn', 'upgradedUsesPerTurn', 'abilities',
  'upgradedAbilities',
])

function extraFields(
  value: Record<string, unknown>,
  knownFields: ReadonlySet<string>,
): Record<string, unknown> {
  return Object.fromEntries(Object.entries(value).filter(([key]) => !knownFields.has(key)))
}

/** 1 effect のデフォルト値。action は attack を初期選択。
 * scope/side は lowercase (Core CardEffectParser が enum 文字列を lowercase で受ける) */
export function emptyEffect(): CardEffect {
  return {
    action: 'attack',
    scope: 'single',
    side: 'enemy',
    amount: 1,
    name: null,
    unitId: null,
    comboMin: null,
    maxComboMin: null,
    pile: null,
    battleOnly: false,
    cardRefId: null,
    select: null,
    amountSource: null,
    trigger: null,
    chancePercent: null,
    costDuration: null,
    cardCount: null,
    statusChanges: null,
  }
}

/** 新規 CardSpec のデフォルト値。Skill / cost 1 / effects=[]。 */
export function emptySpec(): CardSpec {
  return {
    rarity: 1,
    cardType: 'Skill',
    cost: 1,
    upgradedCost: null,
    comboGaugeCost: null,
    effects: [],
    upgradedEffects: null,
    description: null,
    upgradedDescription: null,
    keywords: null,
    upgradedKeywords: null,
    usesPerTurn: null,
    upgradedUsesPerTurn: null,
    abilities: null,
    upgradedAbilities: null,
  }
}

/** 任意 JSON 値を CardEffect に正規化。未知 field は透過保持し、欠損は default で補完。 */
function normalizeEffect(raw: unknown): CardEffect {
  const e = emptyEffect()
  if (!raw || typeof raw !== 'object') return e
  const r = raw as Record<string, unknown>
  e.extraFields = extraFields(r, CARD_EFFECT_FIELDS)
  if (typeof r.action === 'string') e.action = r.action
  if (typeof r.scope === 'string') e.scope = r.scope
  else if (r.scope === null) e.scope = null
  if (typeof r.side === 'string') e.side = r.side
  else if (r.side === null) e.side = null
  if (typeof r.amount === 'number') e.amount = r.amount
  if (typeof r.name === 'string') e.name = r.name
  if (typeof r.unitId === 'string') e.unitId = r.unitId
  if (typeof r.comboMin === 'number') e.comboMin = r.comboMin
  if (typeof r.maxComboMin === 'number') e.maxComboMin = r.maxComboMin
  if (typeof r.pile === 'string') e.pile = r.pile
  if (typeof r.battleOnly === 'boolean') e.battleOnly = r.battleOnly
  if (typeof r.cardRefId === 'string') e.cardRefId = r.cardRefId
  if (typeof r.select === 'string') e.select = r.select
  if (typeof r.amountSource === 'string') e.amountSource = r.amountSource
  if (typeof r.trigger === 'string') e.trigger = r.trigger
  if (typeof r.chancePercent === 'number') e.chancePercent = r.chancePercent
  if (typeof r.costDuration === 'string') e.costDuration = r.costDuration
  if (typeof r.cardCount === 'number') e.cardCount = r.cardCount
  if (Array.isArray(r.statusChanges)) {
    e.statusChanges = r.statusChanges.flatMap((entry) => {
      if (!entry || typeof entry !== 'object') return []
      const change = entry as Record<string, unknown>
      return typeof change.name === 'string' && typeof change.amount === 'number'
        ? [{
            name: change.name,
            amount: change.amount,
            extraFields: extraFields(change, new Set(['name', 'amount'])),
          }]
        : []
    })
  }
  if (e.scope === 'self') e.side = null
  return e
}

function normalizeAbility(raw: unknown): CardAbility | null {
  if (!raw || typeof raw !== 'object') return null
  const ability = raw as Record<string, unknown>
  if (ability.type === 'finisher') return {
    ...extraAbilityFields(ability, ['type']),
    type: 'finisher',
  }
  if (ability.type === 'reprise' && ability.filter && typeof ability.filter === 'object') {
    const filter = ability.filter as Record<string, unknown>
    return {
      ...extraAbilityFields(ability, ['type', 'filter']),
      type: 'reprise',
      filter: {
        extraFields: extraFields(filter, new Set([
          'cardTypes', 'minOriginalCost', 'maxOriginalCost',
          'minPaidCost', 'maxPaidCost', 'requiredKeywords',
        ])),
        cardTypes: Array.isArray(filter.cardTypes)
          ? filter.cardTypes.filter((value): value is string => typeof value === 'string')
          : null,
        minOriginalCost: typeof filter.minOriginalCost === 'number' ? filter.minOriginalCost : null,
        maxOriginalCost: typeof filter.maxOriginalCost === 'number' ? filter.maxOriginalCost : null,
        minPaidCost: typeof filter.minPaidCost === 'number' ? filter.minPaidCost : null,
        maxPaidCost: typeof filter.maxPaidCost === 'number' ? filter.maxPaidCost : null,
        requiredKeywords: Array.isArray(filter.requiredKeywords)
          ? filter.requiredKeywords.filter((value): value is string => typeof value === 'string')
          : null,
      },
    }
  }
  if (ability.type === 'contingency' && ability.trigger && typeof ability.trigger === 'object') {
    const trigger = ability.trigger as Record<string, unknown>
    if (trigger.timing !== 'TurnStart'
      && trigger.timing !== 'BeforeEnemyAttack'
      && trigger.timing !== 'ComboReached') return null
    return {
      ...extraAbilityFields(ability, ['type', 'trigger']),
      type: 'contingency',
      trigger: {
        extraFields: extraFields(trigger, new Set(['timing', 'comboThreshold'])),
        timing: trigger.timing,
        comboThreshold: typeof trigger.comboThreshold === 'number' ? trigger.comboThreshold : null,
      },
    }
  }
  return null
}

function extraAbilityFields(
  ability: Record<string, unknown>,
  knownFields: string[],
): { extraFields: Record<string, unknown> } {
  return { extraFields: extraFields(ability, new Set(knownFields)) }
}

/** raw JSON 文字列を CardSpec にパース。失敗時は emptySpec()。 */
export function parseSpec(json: string): CardSpec {
  let parsed: unknown
  try {
    parsed = JSON.parse(json)
  } catch {
    return emptySpec()
  }
  if (!parsed || typeof parsed !== 'object') return emptySpec()
  const r = parsed as Record<string, unknown>
  const spec = emptySpec()
  spec.extraFields = extraFields(r, CARD_SPEC_FIELDS)
  if (typeof r.rarity === 'number') spec.rarity = r.rarity
  if (typeof r.cardType === 'string') spec.cardType = r.cardType
  if (typeof r.cost === 'number') spec.cost = r.cost
  else if (r.cost === null) spec.cost = null
  if (typeof r.upgradedCost === 'number') spec.upgradedCost = r.upgradedCost
  if (typeof r.comboGaugeCost === 'number') spec.comboGaugeCost = r.comboGaugeCost
  if (Array.isArray(r.effects)) {
    spec.effects = r.effects.map(normalizeEffect)
  }
  if (Array.isArray(r.upgradedEffects)) {
    spec.upgradedEffects = r.upgradedEffects.map(normalizeEffect)
  } else if (r.upgradedEffects === null) {
    spec.upgradedEffects = null
  }
  if (typeof r.description === 'string') spec.description = r.description
  if (typeof r.upgradedDescription === 'string')
    spec.upgradedDescription = r.upgradedDescription
  if (Array.isArray(r.keywords))
    spec.keywords = r.keywords.filter((x): x is string => typeof x === 'string')
  if (Array.isArray(r.upgradedKeywords))
    spec.upgradedKeywords = r.upgradedKeywords.filter(
      (x): x is string => typeof x === 'string',
    )
  if (typeof r.usesPerTurn === 'number') spec.usesPerTurn = r.usesPerTurn
  if (typeof r.upgradedUsesPerTurn === 'number')
    spec.upgradedUsesPerTurn = r.upgradedUsesPerTurn
  if (Array.isArray(r.abilities))
    spec.abilities = r.abilities.flatMap(value => normalizeAbility(value) ?? [])
  if (Array.isArray(r.upgradedAbilities))
    spec.upgradedAbilities = r.upgradedAbilities.flatMap(value => normalizeAbility(value) ?? [])
  return spec
}

/** CardEffect → JSON-shape object (null / default 値の field は省略)。 */
function effectToJsonObject(e: CardEffect): Record<string, unknown> {
  const out: Record<string, unknown> = { ...(e.extraFields ?? {}), action: e.action }
  // amount は always 出す (Core ParseEffect の必須要件: action 依存だが
  //   多くの action で必要)
  const hasGroupedStatuses = (e.action === 'buff' || e.action === 'debuff')
    && e.statusChanges !== null && e.statusChanges.length > 0
  if (hasGroupedStatuses) out.statusChanges = e.statusChanges!.map(statusChangeToJsonObject)
  else out.amount = e.amount
  if (e.scope !== null) out.scope = e.scope
  if (e.side !== null) out.side = e.side
  if (!hasGroupedStatuses && e.name !== null) out.name = e.name
  if (e.unitId !== null) out.unitId = e.unitId
  if (e.comboMin !== null) out.comboMin = e.comboMin
  if (e.maxComboMin !== null) out.maxComboMin = e.maxComboMin
  if (e.pile !== null) out.pile = e.pile
  if (e.battleOnly) out.battleOnly = true
  if (e.cardRefId !== null) out.cardRefId = e.cardRefId
  if (e.select !== null) out.select = e.select
  if (!hasGroupedStatuses && e.amountSource !== null) out.amountSource = e.amountSource
  if (e.trigger !== null) out.trigger = e.trigger
  if (e.chancePercent !== null) out.chancePercent = e.chancePercent
  if (e.costDuration !== null) out.costDuration = e.costDuration
  if (e.cardCount !== null) out.cardCount = e.cardCount
  return out
}

function statusChangeToJsonObject(change: StatusChange): Record<string, unknown> {
  return { ...(change.extraFields ?? {}), name: change.name, amount: change.amount }
}

function abilityToJsonObject(ability: CardAbility): Record<string, unknown> {
  const out: Record<string, unknown> = { ...(ability.extraFields ?? {}), type: ability.type }
  if (ability.type === 'reprise') {
    out.filter = {
      ...(ability.filter.extraFields ?? {}),
      ...(ability.filter.cardTypes !== null ? { cardTypes: ability.filter.cardTypes } : {}),
      ...(ability.filter.minOriginalCost !== null
        ? { minOriginalCost: ability.filter.minOriginalCost } : {}),
      ...(ability.filter.maxOriginalCost !== null
        ? { maxOriginalCost: ability.filter.maxOriginalCost } : {}),
      ...(ability.filter.minPaidCost !== null ? { minPaidCost: ability.filter.minPaidCost } : {}),
      ...(ability.filter.maxPaidCost !== null ? { maxPaidCost: ability.filter.maxPaidCost } : {}),
      ...(ability.filter.requiredKeywords !== null
        ? { requiredKeywords: ability.filter.requiredKeywords } : {}),
    }
  }
  if (ability.type === 'contingency') {
    out.trigger = {
      ...(ability.trigger.extraFields ?? {}),
      timing: ability.trigger.timing,
      ...(ability.trigger.comboThreshold !== null
        ? { comboThreshold: ability.trigger.comboThreshold } : {}),
    }
  }
  return out
}

/** CardSpec → JSON-shape object。null / 空配列の field は省略。 */
export function specToJsonObject(spec: CardSpec): Record<string, unknown> {
  const out: Record<string, unknown> = {
    ...(spec.extraFields ?? {}),
    rarity: spec.rarity,
    cardType: spec.cardType,
  }
  if (spec.cost !== null) out.cost = spec.cost
  if (spec.upgradedCost !== null) out.upgradedCost = spec.upgradedCost
  if (spec.comboGaugeCost !== null) out.comboGaugeCost = spec.comboGaugeCost
  out.effects = spec.effects.map(effectToJsonObject)
  if (spec.upgradedEffects !== null)
    out.upgradedEffects = spec.upgradedEffects.map(effectToJsonObject)
  if (spec.description !== null) out.description = spec.description
  if (spec.upgradedDescription !== null)
    out.upgradedDescription = spec.upgradedDescription
  if (spec.keywords !== null)
    out.keywords = spec.keywords
  if (spec.upgradedKeywords !== null)
    out.upgradedKeywords = spec.upgradedKeywords
  if (spec.usesPerTurn !== null) out.usesPerTurn = spec.usesPerTurn
  if (spec.upgradedUsesPerTurn !== null)
    out.upgradedUsesPerTurn = spec.upgradedUsesPerTurn
  if (spec.abilities !== null) out.abilities = spec.abilities.map(abilityToJsonObject)
  if (spec.upgradedAbilities !== null)
    out.upgradedAbilities = spec.upgradedAbilities.map(abilityToJsonObject)
  return out
}

/** CardSpec → 整形 JSON 文字列 (save 時に使う)。 */
export function specToJson(spec: CardSpec): string {
  return JSON.stringify(specToJsonObject(spec), null, 2)
}

// ============================================================
// Phase 10.5.L1: RelicSpec types
// ============================================================

/**
 * Relic の spec 構造 (versioned 形式の各 version.spec に対応)。
 * Card と異なり relic は upgraded / cost / cardType を持たない。
 * description は手書き必須が基本 (effects から自動生成も可能だが override が標準)。
 *
 * Phase 10.5.L1.5: relic-level trigger フィールドは削除。発動タイミングは
 * 各 effect の trigger 文字列で per-effect 指定する。
 */
export type RelicSpec = {
  rarity: number
  description: string // override (手書き必須が基本、空なら server 側 formatter が effects から自動)
  effects: CardEffect[]
  implemented: boolean
}

export function emptyRelicSpec(): RelicSpec {
  return {
    rarity: 1,
    description: '',
    effects: [],
    implemented: true,
  }
}

export function parseRelicSpec(json: string): RelicSpec {
  let parsed: unknown
  try {
    parsed = JSON.parse(json)
  } catch {
    return emptyRelicSpec()
  }
  if (!parsed || typeof parsed !== 'object') return emptyRelicSpec()
  const r = parsed as Record<string, unknown>
  const spec = emptyRelicSpec()
  if (typeof r.rarity === 'number') spec.rarity = r.rarity
  if (typeof r.description === 'string') spec.description = r.description
  if (Array.isArray(r.effects)) {
    spec.effects = r.effects.map(normalizeEffect)
  }
  if (typeof r.implemented === 'boolean') spec.implemented = r.implemented
  return spec
}

export function relicSpecToJsonObject(spec: RelicSpec): Record<string, unknown> {
  const out: Record<string, unknown> = {
    rarity: spec.rarity,
  }
  // description は relic では空文字でも書く (override 文字列として常に渡す)。
  out.description = spec.description
  // Phase 10.5.L1.5: relic effect は per-effect trigger を保持する。
  //   comboMin は relic 側では意味を持たないため除去する。
  out.effects = spec.effects.map((e) => {
    const obj = effectToJsonObject(e)
    delete obj.comboMin
    delete obj.maxComboMin
    return obj
  })
  out.implemented = spec.implemented
  return out
}

// ---- action ごとに表示する field の map ----

/** 1 effect の表示すべき field 名一覧。action が map に無ければ空配列。 */
const CARD_EFFECT_CONDITIONS: (keyof CardEffect)[] = [
  'comboMin', 'maxComboMin', 'trigger', 'battleOnly',
]

export const EFFECT_ACTION_FIELDS: Record<string, (keyof CardEffect)[]> = {
  attack: ['scope', 'side', 'amount', 'amountSource', ...CARD_EFFECT_CONDITIONS],
  block: ['scope', 'side', 'amount', 'amountSource', ...CARD_EFFECT_CONDITIONS],
  buff: ['scope', 'side', 'statusChanges', ...CARD_EFFECT_CONDITIONS],
  debuff: ['scope', 'side', 'statusChanges', ...CARD_EFFECT_CONDITIONS],
  heal: ['scope', 'side', 'amount', 'amountSource', ...CARD_EFFECT_CONDITIONS],
  draw: ['scope', 'side', 'amount', 'amountSource', ...CARD_EFFECT_CONDITIONS],
  drawCards: ['scope', 'side', 'amount', 'amountSource', ...CARD_EFFECT_CONDITIONS],
  discard: ['scope', 'amount', 'select', ...CARD_EFFECT_CONDITIONS],
  exhaustSelf: [],
  retainSelf: [],
  gainEnergy: ['scope', 'side', 'amount', 'amountSource', ...CARD_EFFECT_CONDITIONS],
  gainComboGauge: ['scope', 'side', 'amount', 'amountSource', ...CARD_EFFECT_CONDITIONS],
  gainMaxEnergy: ['scope', 'side', 'amount', 'amountSource', ...CARD_EFFECT_CONDITIONS],
  // Phase 10.5.M2: exhaustCard / upgrade に select と pile (hand/draw/discard) 拡張。
  // select=all なら amount は無視される。
  exhaustCard: ['amount', 'pile', 'select', ...CARD_EFFECT_CONDITIONS],
  upgrade: ['amount', 'pile', 'select', ...CARD_EFFECT_CONDITIONS],
  summon: ['unitId', 'amount', ...CARD_EFFECT_CONDITIONS],
  selfDamage: ['amount', 'amountSource', ...CARD_EFFECT_CONDITIONS],
  addCard: ['cardRefId', 'amount', 'pile', ...CARD_EFFECT_CONDITIONS],
  recoverFromDiscard: ['amount', 'pile', 'select', ...CARD_EFFECT_CONDITIONS],
  recoverFromExhaust: ['amount', 'pile', 'select', ...CARD_EFFECT_CONDITIONS],
  modifyCardCost: [
    'scope', 'side', 'amount', 'amountSource', 'pile', 'select', 'cardCount',
    'costDuration', ...CARD_EFFECT_CONDITIONS,
  ],

  // Run-state actions used by relics. They remain in the shared editor because
  // the same CardEffect contract backs cards, relics, potions and skills.
  gainMaxHp: ['amount', 'trigger'],
  gainGold: ['amount', 'trigger'],
  healHp: ['amount', 'trigger'],

  // Phase 10.6.B passive modifier actions: amount + trigger ("Passive" 自動セット)
  // unknownTileWeightDelta は name (タイル種別) も使う
  energyPerTurnBonus: ['amount', 'trigger'],
  cardsDrawnPerTurnBonus: ['amount', 'trigger'],
  goldRewardMultiplier: ['amount', 'trigger'],
  shopPriceMultiplier: ['amount', 'trigger'],
  rewardCardChoicesBonus: ['amount', 'trigger'],
  rewardRerollAvailable: ['amount', 'trigger'],
  unknownTileWeightDelta: ['name', 'amount', 'trigger'],
  restHealBonus: ['amount', 'trigger'],
}

/** Phase 10.6.B: 必ず Trigger == "Passive" でしか機能しない action 一覧。
 *  EffectEditor で action を選択した時に trigger を自動的に "Passive" に設定するために使う。 */
export const PASSIVE_ONLY_ACTIONS: ReadonlySet<string> = new Set([
  'energyPerTurnBonus',
  'cardsDrawnPerTurnBonus',
  'goldRewardMultiplier',
  'shopPriceMultiplier',
  'rewardCardChoicesBonus',
  'rewardRerollAvailable',
  'unknownTileWeightDelta',
  'restHealBonus',
])

/** unknownTileWeightDelta の name field に入れる tile kind 一覧。 */
export const UNKNOWN_TILE_KINDS: ReadonlyArray<string> = [
  'enemy', 'elite', 'merchant', 'rest', 'treasure', 'event',
]
