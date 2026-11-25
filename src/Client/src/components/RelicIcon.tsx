import { useMemo } from 'react'
import { useTooltipTarget } from './Tooltip'
import type { RelicCatalog } from '../api/catalog'
import { getRelicTooltipContent } from './relicTooltip'
import { relicIcon } from '../presentation/assets'
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
  const rarityKey = entry ? rarityClassOf(entry.rarity) : 'common'
  const rarityClass = `relic-icon--${rarityKey}`
  const content = useMemo(
    () => getRelicTooltipContent(id, catalog, names),
    [id, catalog, names],
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
        <img src={relicIcon(id)} alt="" draggable={false} />
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
