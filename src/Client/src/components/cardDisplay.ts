import type { CardCatalog } from '../api/catalog'
import type { CardRarity, CardType } from './Card'

export type CardDisplay = {
  name: string
  cost: number | string
  type: CardType
  rarity: CardRarity
}

const TYPE_MAP: Record<string, CardType> = {
  attack: 'attack',
  skill: 'skill',
  power: 'power',
  curse: 'curse',
  status: 'status',
}

function rarityFromNumber(n: number): CardRarity {
  switch (n) {
    case 0: return 'c'
    case 1: return 'r'
    case 2: return 'e'
    case 3: return 'l'
    default: return 'c'
  }
}

export function cardDisplay(
  id: string,
  catalog: CardCatalog | null,
  nameFallback?: string,
): CardDisplay {
  const entry = catalog?.[id]
  if (!entry) {
    return {
      name: nameFallback ?? id,
      cost: 1,
      type: 'skill',
      rarity: 'c',
    }
  }
  return {
    name: entry.displayName ?? entry.name,
    cost: entry.cost ?? 0,
    type: TYPE_MAP[entry.cardType.toLowerCase()] ?? 'skill',
    rarity: rarityFromNumber(entry.rarity),
  }
}
