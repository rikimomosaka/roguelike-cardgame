import { apiRequest } from './client'

export type CardCatalogEntry = {
  id: string
  name: string
  displayName: string | null
  rarity: number
  cardType: string
  cost: number | null
}

export type CardCatalog = Record<string, CardCatalogEntry>

let cache: Promise<CardCatalog> | null = null

export function getCardCatalog(): Promise<CardCatalog> {
  if (cache === null) {
    cache = apiRequest<CardCatalog>('GET', '/catalog/cards').catch((err) => {
      cache = null
      throw err
    })
  }
  return cache
}

// Exposed for tests that need to reset the module-level cache.
export function resetCardCatalogCacheForTests(): void {
  cache = null
}
