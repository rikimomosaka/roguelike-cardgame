import type { CardRarity } from './Card'

/** API values mirror Core.Cards.CardRarity: Promo=0 through Token=5. */
export function rarityCodeFromNumber(value: number): CardRarity {
  switch (value) {
    case 2: return 'r'
    case 3: return 'e'
    case 4: return 'l'
    case 5: return 't'
    case 0: // Promo currently shares the common presentation.
    case 1:
    default:
      return 'c'
  }
}

export function optionalRarityCodeFromNumber(
  value: number | undefined,
): CardRarity | undefined {
  return value === undefined ? undefined : rarityCodeFromNumber(value)
}

export function rarityCodeFromName(
  rarity: string | undefined,
): CardRarity | undefined {
  if (!rarity) return undefined
  switch (rarity.toLowerCase()) {
    case 'rare':
    case 'r':
      return 'r'
    case 'epic':
    case 'e':
      return 'e'
    case 'legendary':
    case 'l':
      return 'l'
    case 'token':
    case 't':
      return 't'
    default:
      return 'c'
  }
}
