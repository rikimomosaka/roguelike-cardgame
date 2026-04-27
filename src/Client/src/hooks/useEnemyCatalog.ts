import { useEffect, useState } from 'react'
import { fetchEnemyCatalog, type EnemyCatalog } from '../api/catalog'

export type EnemyNameMap = Record<string, string>

export function useEnemyCatalog(): {
  catalog: EnemyCatalog | null
  names: EnemyNameMap
  loading: boolean
} {
  const [catalog, setCatalog] = useState<EnemyCatalog | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let cancelled = false
    fetchEnemyCatalog()
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

  const names: EnemyNameMap = {}
  if (catalog) {
    for (const [id, entry] of Object.entries(catalog)) {
      names[id] = entry.name
    }
  }
  return { catalog, names, loading }
}
