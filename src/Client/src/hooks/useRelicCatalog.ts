import { useEffect, useState } from 'react'
import { getRelicCatalog, type RelicCatalog } from '../api/catalog'

export type RelicNameMap = Record<string, string>

export function useRelicCatalog(): { catalog: RelicCatalog | null; names: RelicNameMap } {
  const [catalog, setCatalog] = useState<RelicCatalog | null>(null)

  useEffect(() => {
    let cancelled = false
    getRelicCatalog()
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

  const names: RelicNameMap = {}
  if (catalog) {
    for (const [id, entry] of Object.entries(catalog)) {
      names[id] = entry.name
    }
  }
  return { catalog, names }
}
