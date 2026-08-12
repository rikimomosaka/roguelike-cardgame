import type { CardRarity } from '../components/Card'

export function rarityToItemClass(rarity: CardRarity | undefined): string {
  switch (rarity) {
    case 'r': return 'item--rare'
    case 'e': return 'item--epic'
    case 'l': return 'item--legendary'
    case 'c':
    default:
      return 'item--common'
  }
}
