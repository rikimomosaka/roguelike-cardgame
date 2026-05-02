import { useMemo } from 'react'
import type { CardRarity } from './Card'
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
  // M6.3: 効果テキストとフレーバーを分離して tooltip に渡す。
  //   effectText 単体を desc に、flavor は専用スロットへ。古い catalog (effectText
  //   未提供) には description フォールバックで動作。
  const effectText = entry?.effectText ?? ''
  const flavor = entry?.flavor ?? ''
  const fallbackDesc = effectText.length > 0 ? effectText : entry?.description ?? ''
  const rarityKey = entry ? rarityClassOf(entry.rarity) : 'common'
  const rarityClass = `relic-icon--${rarityKey}`
  const rarityCode = rarityCodeFromKey(rarityKey)
  const content = useMemo(
    () => ({
      name,
      rarity: rarityCode,
      desc: fallbackDesc || '—',
      flavor: flavor || undefined,
    }),
    [name, rarityCode, fallbackDesc, flavor],
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
        <img src={`/icons/relics/${id}.png`} alt="" draggable={false} />
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

function rarityCodeFromKey(key: string): CardRarity {
  switch (key) {
    case 'rare': return 'r'
    case 'epic': return 'e'
    case 'legendary': return 'l'
    default: return 'c'
  }
}

