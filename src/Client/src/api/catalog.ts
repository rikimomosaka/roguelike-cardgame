import { apiRequest } from './client'

export type CardCatalogEntry = {
  id: string
  name: string
  displayName: string | null
  rarity: number
  cardType: string
  cost: number | null
}

export type PotionCatalogEntry = {
  id: string
  name: string
  rarity: number
  usableInBattle: boolean
  usableOutOfBattle: boolean
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
