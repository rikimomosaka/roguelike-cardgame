import { apiRequest } from './client'
import type {
  CharacterCatalogEntryDto,
  EnemyCatalogEntryDto,
  UnitCatalogEntryDto,
} from './types'

export type CardCatalogEntry = {
  id: string
  name: string
  displayName: string | null
  rarity: number
  cardType: string
  cost: number | null
  upgradedCost: number | null
  upgradable: boolean
  description: string
  upgradedDescription: string | null
  comboGaugeCost?: number
  usesPerTurn?: number
  upgradedUsesPerTurn?: number | null
  /** Added by the pool-aware catalog; optional keeps cached/legacy fixtures readable. */
  poolKinds?: CardPoolKind[]
  poolClass?: CardPoolClass | null
  eligibleCharacterIds?: string[]
}

export type CharacterClass = 'Striker' | 'Guardian' | 'Tactician' | 'Enhancer'
export type CardPoolClass = 'Universal' | CharacterClass
export type CardPoolKind = 'Basic' | 'Reward'

export type PotionCatalogEntry = {
  id: string
  name: string
  rarity: number
  usableOutsideBattle: boolean
  description: string
  usesPerTurn?: number
}

export type CardCatalog = Record<string, CardCatalogEntry>
export type PotionCatalog = Record<string, PotionCatalogEntry>

let cardCache: Promise<CardCatalog> | null = null
let potionCache: Promise<PotionCatalog> | null = null

export function getCardCatalog(): Promise<CardCatalog> {
  if (cardCache === null) {
    cardCache = apiRequest<CardCatalog>('GET', '/catalog/cards').catch((err) => {
      cardCache = null
      throw err
    })
  }
  return cardCache
}

export function getPotionCatalog(): Promise<PotionCatalog> {
  if (potionCache === null) {
    potionCache = apiRequest<PotionCatalog>('GET', '/catalog/potions').catch(
      (err) => {
        potionCache = null
        throw err
      },
    )
  }
  return potionCache
}

/** カードカタログを取り直した時に呼ぶ購読者（useCardCatalog が描き直すため）。 */
const cardCatalogListeners = new Set<(catalog: CardCatalog) => void>()

export function subscribeCardCatalog(listener: (catalog: CardCatalog) => void): () => void {
  cardCatalogListeners.add(listener)
  return () => { cardCatalogListeners.delete(listener) }
}

/** カードカタログを取り直す（ページを開いた後にサーバーでカードが増えた時）。 */
export function refreshCardCatalog(): Promise<CardCatalog> {
  cardCache = null
  const next = getCardCatalog()
  next.then(catalog => cardCatalogListeners.forEach(listener => listener(catalog))).catch(() => {})
  return next
}

const refreshedForMissingIds = new Set<string>()

/**
 * カード ID の表示名。カタログの取得を待ち、ID が無ければ一度だけ取り直す。それでも無ければ ID を返す。
 */
export async function resolveCardName(cardId: string): Promise<string> {
  try {
    let catalog = await getCardCatalog()
    if (!catalog[cardId] && !refreshedForMissingIds.has(cardId)) {
      refreshedForMissingIds.add(cardId)
      catalog = await refreshCardCatalog()
    }
    const entry = catalog[cardId]
    return entry?.displayName ?? entry?.name ?? cardId
  } catch {
    return cardId
  }
}

/** 表示するカードのうちカタログに無いものがあれば、一度だけカタログを取り直す（同じ ID では繰り返さない）。 */
export function refreshCardCatalogIfMissing(catalog: CardCatalog | null, cardIds: readonly string[]): void {
  if (!catalog) return
  const missing = cardIds.filter(id => !catalog[id] && !refreshedForMissingIds.has(id))
  if (missing.length === 0) return
  missing.forEach(id => refreshedForMissingIds.add(id))
  void refreshCardCatalog().catch(() => {})
}

export function resetCardCatalogCacheForTests(): void {
  cardCache = null
  refreshedForMissingIds.clear()
}

export function resetPotionCatalogCacheForTests(): void {
  potionCache = null
}

/**
 * Phase 10.5.L1.5: trigger フィールド廃止。発動タイミングは effects[].trigger で
 * per-effect 管理されるため catalog DTO に冗長に含めない。
 *
 * Phase 10.5.M6.3: tooltip 層別レイアウト用に effectText / flavor を分離。
 * description は combined のまま (既存利用箇所の後方互換)。
 */
export type RelicCatalogEntry = {
  id: string
  name: string
  description: string
  effectText: string
  flavor: string
  rarity: string
}

export type EventChoiceCatalogEntry = {
  label: string
  conditionSummary: string | null
  effectSummaries: string[]
}

export type EventCatalogEntry = {
  id: string
  name: string
  description: string
  choices: EventChoiceCatalogEntry[]
}

export type RelicCatalog = Record<string, RelicCatalogEntry>
export type EventCatalog = Record<string, EventCatalogEntry>

let relicCache: Promise<RelicCatalog> | null = null
let eventCache: Promise<EventCatalog> | null = null

export function getRelicCatalog(): Promise<RelicCatalog> {
  if (relicCache === null) {
    relicCache = apiRequest<RelicCatalogEntry[]>('GET', '/catalog/relics')
      .then((list) => Object.fromEntries(list.map((r) => [r.id, r])))
      .catch((err) => {
        relicCache = null
        throw err
      })
  }
  return relicCache
}

export function getEventCatalog(): Promise<EventCatalog> {
  if (eventCache === null) {
    eventCache = apiRequest<EventCatalogEntry[]>('GET', '/catalog/events')
      .then((list) => Object.fromEntries(list.map((e) => [e.id, e])))
      .catch((err) => {
        eventCache = null
        throw err
      })
  }
  return eventCache
}

export function resetRelicCatalogCacheForTests(): void {
  relicCache = null
}

export function resetEventCatalogCacheForTests(): void {
  eventCache = null
}

// ===== Phase 10.3-MVP: Enemy / Unit catalogs =====

export type EnemyCatalog = Record<string, EnemyCatalogEntryDto>
export type UnitCatalog = Record<string, UnitCatalogEntryDto>

export async function fetchEnemyCatalog(): Promise<EnemyCatalog> {
  return await apiRequest<EnemyCatalog>('GET', '/catalog/enemies', {})
}

export async function fetchUnitCatalog(): Promise<UnitCatalog> {
  return await apiRequest<UnitCatalog>('GET', '/catalog/units', {})
}

export type CharacterCatalog = Record<string, CharacterCatalogEntryDto>

export async function fetchCharacterCatalog(): Promise<CharacterCatalog> {
  return await apiRequest<CharacterCatalog>('GET', '/catalog/characters', {})
}
