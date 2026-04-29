import { useEffect, useState } from 'react'
import { fetchCharacterCatalog, type CharacterCatalog } from '../api/catalog'

export type CharacterNameMap = Record<string, string>

export function useCharacterCatalog(): {
  catalog: CharacterCatalog | null
  names: CharacterNameMap
  loading: boolean
} {
  const [catalog, setCatalog] = useState<CharacterCatalog | null>(null)
  const [loading, setLoading] = useState(true)

  useEffect(() => {
    let cancelled = false
    fetchCharacterCatalog()
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

  const names: CharacterNameMap = {}
  if (catalog) {
    for (const [id, entry] of Object.entries(catalog)) {
      names[id] = entry.name
    }
  }
  return { catalog, names, loading }
}
