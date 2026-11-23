import type { CardCatalog, CardPoolClass } from '../api/catalog'
import type { CardRarity, CardType } from './Card'
import { rarityCodeFromNumber } from './rarity'

export type CardDisplay = {
  name: string
  cost: number | string
  type: CardType
  rarity: CardRarity
  poolClass: CardPoolClass | null
  description: string
  upgradedDescription: string | null
}

const TYPE_MAP: Record<string, CardType> = {
  attack: 'attack',
  skill: 'skill',
  power: 'power',
  curse: 'curse',
  status: 'status',
  unit: 'unit',
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
      poolClass: null,
      description: '',
      upgradedDescription: null,
    }
  }
  return {
    name: entry.displayName ?? entry.name,
    cost: entry.cost ?? 0,
    type: TYPE_MAP[entry.cardType.toLowerCase()] ?? 'skill',
    rarity: rarityCodeFromNumber(entry.rarity),
    poolClass: entry.poolClass ?? null,
    description: entry.description ?? '',
    upgradedDescription: entry.upgradedDescription ?? null,
  }
}
