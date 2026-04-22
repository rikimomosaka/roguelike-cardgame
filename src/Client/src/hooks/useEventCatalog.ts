import { useEffect, useState } from 'react'
import { getEventCatalog, type EventCatalog } from '../api/catalog'

export type EventNameMap = Record<string, string>

export function useEventCatalog(): { catalog: EventCatalog | null; names: EventNameMap } {
  const [catalog, setCatalog] = useState<EventCatalog | null>(null)

  useEffect(() => {
    let cancelled = false
    getEventCatalog()
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

  const names: EventNameMap = {}
  if (catalog) {
    for (const [id, entry] of Object.entries(catalog)) {
      names[id] = entry.name
    }
  }
  return { catalog, names }
}
