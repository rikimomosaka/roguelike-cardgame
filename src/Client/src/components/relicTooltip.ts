import type { RelicCatalog } from '../api/catalog'
import type { CardRarity } from './Card'
import type { TooltipContent } from './Tooltip'

/** Canonical relic tooltip shared by every relic presentation. */
export function getRelicTooltipContent(
  id: string,
  catalog: RelicCatalog | null,
  names: Record<string, string>,
): TooltipContent {
  const entry = catalog?.[id] ?? null
  const name = entry?.name ?? names[id] ?? id
  const effectText = entry?.effectText ?? ''
  const flavor = entry?.flavor ?? ''
  // Current catalog `description` is effectText + flavor for compatibility.
  // When only flavor exists, reusing it as desc would render the same prose twice.
  const fallbackDesc = effectText.length > 0
    ? effectText
    : flavor.length > 0
      ? ''
      : entry?.description ?? ''

  return {
    name,
    rarity: rarityCode(entry?.rarity),
    desc: fallbackDesc || '—',
    flavor: flavor || undefined,
  }
}

function rarityCode(rarity: string | undefined): CardRarity {
  switch (rarity?.toLowerCase()) {
    case 'rare':
    case 'r':
      return 'r'
    case 'epic':
    case 'e':
      return 'e'
    case 'legendary':
    case 'l':
      return 'l'
    default:
      return 'c'
  }
}
