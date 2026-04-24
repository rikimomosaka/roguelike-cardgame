import { useMemo } from 'react'
import { useTooltipTarget } from './Tooltip'
import type { RelicCatalog } from '../api/catalog'
import './RelicIcon.css'

type Props = {
  id: string
  catalog: RelicCatalog | null
  names: Record<string, string>
}

export function RelicIcon({ id, catalog, names }: Props) {
  const entry = catalog?.[id] ?? null
  const name = entry?.name ?? names[id] ?? id
  const desc = entry?.description ?? ''
  const rarityClass = entry ? `relic-icon--${rarityClassOf(entry.rarity)}` : ''
  const content = useMemo(
    () => ({ name, desc: desc || '—' }),
    [name, desc],
  )
  const tip = useTooltipTarget(content)
  return (
    <span
      className={`relic-icon ${rarityClass}`}
      aria-label={name}
      tabIndex={0}
      {...tip}
    >
      <span className="relic-icon__sprite" aria-hidden="true">
        {pixelGlyphFor(id)}
      </span>
    </span>
  )
}

function rarityClassOf(rarity: string): string {
  const r = rarity.toLowerCase()
  if (r === 'rare' || r === 'r') return 'rare'
  if (r === 'epic' || r === 'e') return 'epic'
  if (r === 'legendary' || r === 'l') return 'legendary'
  return 'common'
}

// Deterministic placeholder glyph until real dot-art sprites arrive.
// Picks one of a small set of Unicode block pictograms based on id hash.
const GLYPHS = ['◈', '◉', '◆', '▲', '●', '■', '♆', '✦', '☘', '♠', '✧', '◊']
function pixelGlyphFor(id: string): string {
  let h = 0
  for (let i = 0; i < id.length; i++) h = (h * 31 + id.charCodeAt(i)) >>> 0
  return GLYPHS[h % GLYPHS.length]
}
