import { useEffect, useState } from 'react'
import {
  getCardCatalog,
  getPotionCatalog,
  type CardCatalog,
  type PotionCatalog,
} from '../api/catalog'

export type CardNameMap = Record<string, string>
export type PotionNameMap = Record<string, string>

// Resolves card IDs to their display-friendly name (Japanese),
// falling back to the raw ID until the catalog is loaded.
export function useCardCatalog(): { names: CardNameMap; catalog: CardCatalog | null } {
  const [catalog, setCatalog] = useState<CardCatalog | null>(null)

  useEffect(() => {
    let cancelled = false
    getCardCatalog()
      .then((c) => {
        if (!cancelled) setCatalog(c)
      })
      .catch(() => {
        if (!cancelled) setCatalog(null)
      })
    return () => {
      cancelled = true
    }
  }, [])

  const names: CardNameMap = {}
  if (catalog) {
    for (const [id, entry] of Object.entries(catalog)) {
      names[id] = entry.displayName ?? entry.name
    }
  }

  return { names, catalog }
}

export function usePotionCatalog(): { names: PotionNameMap; catalog: PotionCatalog | null } {
  const [catalog, setCatalog] = useState<PotionCatalog | null>(null)

  useEffect(() => {
    let cancelled = false
    getPotionCatalog()
      .then((c) => {
        if (!cancelled) setCatalog(c)
      })
      .catch(() => {
        if (!cancelled) setCatalog(null)
      })
    return () => {
      cancelled = true
    }
  }, [])

  const names: PotionNameMap = {}
  if (catalog) {
    for (const [id, entry] of Object.entries(catalog)) {
      names[id] = entry.name
    }
  }

  return { names, catalog }
}
