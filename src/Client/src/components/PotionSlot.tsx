import { useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { usePotionCatalog } from '../hooks/useCardCatalog'
import type { CardRarity } from './Card'
import { useTooltipTarget } from './Tooltip'
import type { TooltipContent } from './Tooltip'

type Props = {
  slotIndex: number
  potionId: string
  onDiscard: () => void
}

function potionRarityCode(n: number): CardRarity {
  switch (n) {
    case 0: return 'c'
    case 1: return 'r'
    case 2: return 'e'
    case 3: return 'l'
    default: return 'c'
  }
}

export function PotionSlot({ slotIndex, potionId, onDiscard }: Props) {
  const [menuOpen, setMenuOpen] = useState(false)
  const [menuPos, setMenuPos] = useState<{ left: number; top: number } | null>(null)
  const iconRef = useRef<HTMLButtonElement>(null)
  const filled = potionId !== ''
  const { catalog: potionCatalog } = usePotionCatalog()
  const tooltipContent = useMemo<TooltipContent | null>(() => {
    if (!filled) return null
    const entry = potionCatalog?.[potionId]
    const name = entry?.name ?? potionId
    const desc = entry?.description ?? '—'
    const rarity: CardRarity | undefined =
      entry !== undefined ? potionRarityCode(entry.rarity) : undefined
    return { name, rarity, desc }
  }, [filled, potionCatalog, potionId])
  const tip = useTooltipTarget(tooltipContent)

  useEffect(() => {
    if (!menuOpen || !iconRef.current) return
    const rect = iconRef.current.getBoundingClientRect()
    setMenuPos({ left: rect.right - 96, top: rect.bottom + 6 })
  }, [menuOpen])

  useEffect(() => {
    if (!menuOpen) return
    const onDoc = (e: MouseEvent) => {
      const t = e.target as Node | null
      if (iconRef.current && t && iconRef.current.contains(t)) return
      setMenuOpen(false)
    }
    window.addEventListener('mousedown', onDoc)
    return () => window.removeEventListener('mousedown', onDoc)
  }, [menuOpen])

  if (!filled) {
    return (
      <div
        className="potion-slot potion-slot--empty"
        aria-label={`スロット ${slotIndex + 1} (空)`}
      />
    )
  }

  return (
    <div className="potion-slot" aria-label={`スロット ${slotIndex + 1}: ${potionId}`}>
      <button
        ref={iconRef}
        className="potion-slot__icon"
        onClick={() => setMenuOpen(v => !v)}
        onMouseEnter={tip.onMouseEnter}
        onMouseMove={tip.onMouseMove}
        onMouseLeave={tip.onMouseLeave}
        aria-label={`ポーション: ${potionId}`}
      >
        <img src={`/icons/potions/${potionId}.png`} alt="" draggable={false} />
      </button>
      {menuOpen && menuPos && createPortal(
        <div
          className="potion-slot__menu"
          role="menu"
          style={{ left: menuPos.left, top: menuPos.top }}
          onMouseDown={e => e.stopPropagation()}
        >
          <button
            onClick={() => {
              onDiscard()
              setMenuOpen(false)
            }}
          >
            捨てる
          </button>
          <button onClick={() => setMenuOpen(false)}>キャンセル</button>
        </div>,
        document.body,
      )}
    </div>
  )
}
