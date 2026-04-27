import { useEffect, useState } from 'react'
import { fetchUnitCatalog, type UnitCatalog } from '../api/catalog'

export type UnitNameMap = Record<string, string>

export function useUnitCatalog(): {
  catalog: UnitCatalog | null
  names: UnitNameMap
  loading: boolean
} {
  const [catalog, setCatalog] = useState<UnitCatalog | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let cancelled = false
    fetchUnitCatalog()
      .then((c) => {
        if (!cancelled) {
          setCatalog(c)
          setLoading(false)
        }
      })
      .catch(() => {
        if (!cancelled) {
          setCatalog(null)
          setLoading(false)
        }
      })
    return () => {
      cancelled = true
    }
  }, [])

  const names: UnitNameMap = {}
  if (catalog) {
    for (const [id, entry] of Object.entries(catalog)) {
      names[id] = entry.name
    }
  }
  return { catalog, names, loading }
}
