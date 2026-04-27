import { apiRequest } from './client'
import type { EnemyCatalogEntryDto, UnitCatalogEntryDto } from './types'

export type CardCatalogEntry = {
  id: string
  name: string
  displayName: string | null
  rarity: number
  cardType: string
  cost: number | null
  upgradable: boolean
  description: string
  upgradedDescription: string | null
}

export type PotionCatalogEntry = {
  id: string
  name: string
  rarity: number
  usableOutsideBattle: boolean
  description: string
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

export function resetCardCatalogCacheForTests(): void {
  cardCache = null
}

export function resetPotionCatalogCacheForTests(): void {
  potionCache = null
}

export type RelicCatalogEntry = {
  id: string
  name: string
  description: string
  rarity: string
  trigger: string
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
