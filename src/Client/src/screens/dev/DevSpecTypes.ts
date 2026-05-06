// Phase 10.5.M: 構造化フォームで扱う CardSpec / CardEffect の TS 型 + 補助関数。
//
// CardSpec の JSON shape は server/Core が期待するものと一致させる。
// null フィールドは specToJson で省略する (CardJsonLoader 互換のため)。

export type CardEffect = {
  action: string
  scope: string | null
  side: string | null
  amount: number
  name: string | null
  unitId: string | null
  comboMin: number | null
  pile: string | null
  battleOnly: boolean
  cardRefId: string | null
  select: string | null
  amountSource: string | null
  trigger: string | null
}

export type CardSpec = {
  rarity: number
  cardType: string
  cost: number | null
  upgradedCost: number | null
  effects: CardEffect[]
  upgradedEffects: CardEffect[] | null
  description: string | null
  upgradedDescription: string | null
  keywords: string[] | null
  upgradedKeywords: string[] | null
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
    pile: null,
    battleOnly: false,
    cardRefId: null,
    select: null,
    amountSource: null,
    trigger: null,
  }
}

/** 新規 CardSpec のデフォルト値。Skill / cost 1 / effects=[]。 */
export function emptySpec(): CardSpec {
  return {
    rarity: 1,
    cardType: 'Skill',
    cost: 1,
    upgradedCost: null,
    effects: [],
    upgradedEffects: null,
    description: null,
    upgradedDescription: null,
    keywords: null,
    upgradedKeywords: null,
  }
}

/** 任意 JSON 値を CardEffect に正規化。未知 field は無視、欠損は default で補完。 */
function normalizeEffect(raw: unknown): CardEffect {
  const e = emptyEffect()
  if (!raw || typeof raw !== 'object') return e
  const r = raw as Record<string, unknown>
  if (typeof r.action === 'string') e.action = r.action
  if (typeof r.scope === 'string') e.scope = r.scope
  else if (r.scope === null) e.scope = null
  if (typeof r.side === 'string') e.side = r.side
  else if (r.side === null) e.side = null
  if (typeof r.amount === 'number') e.amount = r.amount
  if (typeof r.name === 'string') e.name = r.name
  if (typeof r.unitId === 'string') e.unitId = r.unitId
  if (typeof r.comboMin === 'number') e.comboMin = r.comboMin
  if (typeof r.pile === 'string') e.pile = r.pile
  if (typeof r.battleOnly === 'boolean') e.battleOnly = r.battleOnly
  if (typeof r.cardRefId === 'string') e.cardRefId = r.cardRefId
  if (typeof r.select === 'string') e.select = r.select
  if (typeof r.amountSource === 'string') e.amountSource = r.amountSource
  if (typeof r.trigger === 'string') e.trigger = r.trigger
  return e
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
  if (typeof r.rarity === 'number') spec.rarity = r.rarity
  if (typeof r.cardType === 'string') spec.cardType = r.cardType
  if (typeof r.cost === 'number') spec.cost = r.cost
  else if (r.cost === null) spec.cost = null
  if (typeof r.upgradedCost === 'number') spec.upgradedCost = r.upgradedCost
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
  return spec
}

/** CardEffect → JSON-shape object (null / default 値の field は省略)。 */
function effectToJsonObject(e: CardEffect): Record<string, unknown> {
  const out: Record<string, unknown> = { action: e.action }
  // amount は always 出す (Core ParseEffect の必須要件: action 依存だが
  //   多くの action で必要)
  out.amount = e.amount
  if (e.scope !== null) out.scope = e.scope
  if (e.side !== null) out.side = e.side
  if (e.name !== null) out.name = e.name
  if (e.unitId !== null) out.unitId = e.unitId
  if (e.comboMin !== null) out.comboMin = e.comboMin
  if (e.pile !== null) out.pile = e.pile
  if (e.battleOnly) out.battleOnly = true
  if (e.cardRefId !== null) out.cardRefId = e.cardRefId
  if (e.select !== null) out.select = e.select
  if (e.amountSource !== null) out.amountSource = e.amountSource
  if (e.trigger !== null) out.trigger = e.trigger
  return out
}

/** CardSpec → JSON-shape object。null / 空配列の field は省略。 */
export function specToJsonObject(spec: CardSpec): Record<string, unknown> {
  const out: Record<string, unknown> = {
    rarity: spec.rarity,
    cardType: spec.cardType,
  }
  if (spec.cost !== null) out.cost = spec.cost
  if (spec.upgradedCost !== null) out.upgradedCost = spec.upgradedCost
  out.effects = spec.effects.map(effectToJsonObject)
  if (spec.upgradedEffects !== null)
    out.upgradedEffects = spec.upgradedEffects.map(effectToJsonObject)
  if (spec.description !== null) out.description = spec.description
  if (spec.upgradedDescription !== null)
    out.upgradedDescription = spec.upgradedDescription
  if (spec.keywords !== null && spec.keywords.length > 0)
    out.keywords = spec.keywords
  if (spec.upgradedKeywords !== null && spec.upgradedKeywords.length > 0)
    out.upgradedKeywords = spec.upgradedKeywords
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
    return obj
  })
  out.implemented = spec.implemented
  return out
}

// ---- action ごとに表示する field の map ----

/** 1 effect の表示すべき field 名一覧。action が map に無ければ空配列。 */
export const EFFECT_ACTION_FIELDS: Record<string, (keyof CardEffect)[]> = {
  attack: ['scope', 'side', 'amount', 'amountSource', 'trigger'],
  block: ['scope', 'side', 'amount', 'amountSource', 'trigger'],
  buff: ['scope', 'side', 'name', 'amount', 'comboMin', 'trigger'],
  debuff: ['scope', 'side', 'name', 'amount', 'comboMin', 'trigger'],
  heal: ['scope', 'side', 'amount', 'trigger'],
  draw: ['amount', 'amountSource', 'trigger'],
  drawCards: ['amount', 'amountSource', 'trigger'],
  discard: ['scope', 'amount', 'select', 'trigger'],
  exhaustSelf: [],
  retainSelf: [],
  gainEnergy: ['amount', 'trigger'],
  gainMaxEnergy: ['amount', 'trigger'],
  // Phase 10.5.M2: exhaustCard / upgrade に select と pile (hand/draw/discard) 拡張。
  // select=all なら amount は無視される。
  exhaustCard: ['amount', 'pile', 'select', 'trigger'],
  upgrade: ['amount', 'pile', 'select', 'trigger'],
  summon: ['unitId', 'amount', 'trigger'],
  selfDamage: ['amount', 'trigger'],
  addCard: ['cardRefId', 'amount', 'pile', 'trigger'],
  recoverFromDiscard: ['amount', 'pile', 'select', 'trigger'],

  // Phase 10.6.B passive modifier actions: amount + trigger ("Passive" を選択する必要)
  energyPerTurnBonus: ['amount', 'trigger'],
  cardsDrawnPerTurnBonus: ['amount', 'trigger'],
  goldRewardMultiplier: ['amount', 'trigger'],
  shopPriceMultiplier: ['amount', 'trigger'],
  rewardCardChoicesBonus: ['amount', 'trigger'],
  rewardRerollAvailable: ['amount', 'trigger'],
  unknownEnemyWeightDelta: ['amount', 'trigger'],
  unknownEliteWeightDelta: ['amount', 'trigger'],
  unknownMerchantWeightDelta: ['amount', 'trigger'],
  unknownRestWeightDelta: ['amount', 'trigger'],
  unknownTreasureWeightDelta: ['amount', 'trigger'],
  unknownEventWeightDelta: ['amount', 'trigger'],
  restHealBonus: ['amount', 'trigger'],
}
