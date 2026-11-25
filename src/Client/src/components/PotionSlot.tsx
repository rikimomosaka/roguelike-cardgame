import { useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { usePotionCatalog } from '../hooks/useCardCatalog'
import type { CardRarity } from './Card'
import { rarityCodeFromNumber } from './rarity'
import { useTooltipTarget } from './Tooltip'
import type { TooltipContent } from './Tooltip'
import { potionIcon } from '../presentation/assets'
import './PotionSlot.css'

type Props = {
  slotIndex: number
  potionId: string
  onDiscard: () => void
  /** 戦闘中の使用ハンドラ。指定されたときメニューに「使う」を表示する。 */
  onUse?: () => void
  outsideBattleUseOnly?: boolean
  usesThisTurn?: number
}

export function PotionSlot({ slotIndex, potionId, onDiscard, onUse, outsideBattleUseOnly = false, usesThisTurn = 0 }: Props) {
  const [menuOpen, setMenuOpen] = useState(false)
  const [menuPos, setMenuPos] = useState<{ left: number; top: number } | null>(null)
  const iconRef = useRef<HTMLButtonElement>(null)
  const filled = potionId !== ''
  const { catalog: potionCatalog } = usePotionCatalog()
  const usageLimit = potionCatalog?.[potionId]?.usesPerTurn ?? 0
  const usageLimitReached = usageLimit > 0 && usesThisTurn >= usageLimit
  const canUse = onUse !== undefined && !usageLimitReached &&
    (!outsideBattleUseOnly || potionCatalog?.[potionId]?.usableOutsideBattle === true)
  const tooltipContent = useMemo<TooltipContent | null>(() => {
    if (!filled) return null
    const entry = potionCatalog?.[potionId]
    const name = entry?.name ?? potionId
    const desc = entry?.description?.replace(
      `(0/${usageLimit})`,
      `(${Math.min(Math.max(0, usesThisTurn), usageLimit)}/${usageLimit})`,
    ) ?? '—'
    const rarity: CardRarity | undefined =
      entry !== undefined ? rarityCodeFromNumber(entry.rarity) : undefined
    return { name, rarity, desc }
  }, [filled, potionCatalog, potionId, usageLimit, usesThisTurn])
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
        <img src={potionIcon(potionId)} alt="" draggable={false} />
      </button>
      {menuOpen && menuPos && createPortal(
        <div
          className="potion-slot__menu"
          role="menu"
          style={{ left: menuPos.left, top: menuPos.top }}
          onMouseDown={e => e.stopPropagation()}
        >
          {canUse ? (
            <button
              type="button"
              onClick={() => {
                onUse()
                setMenuOpen(false)
              }}
            >
              使う
            </button>
          ) : null}
          <button
            type="button"
            onClick={() => {
              onDiscard()
              setMenuOpen(false)
            }}
          >
            捨てる
          </button>
          <button type="button" onClick={() => setMenuOpen(false)}>キャンセル</button>
        </div>,
        document.body,
      )}
    </div>
  )
}
