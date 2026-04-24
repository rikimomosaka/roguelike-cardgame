import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'

type Props = {
  slotIndex: number
  potionId: string
  onDiscard: () => void
}

export function PotionSlot({ slotIndex, potionId, onDiscard }: Props) {
  const [menuOpen, setMenuOpen] = useState(false)
  const [menuPos, setMenuPos] = useState<{ left: number; top: number } | null>(null)
  const iconRef = useRef<HTMLButtonElement>(null)
  const filled = potionId !== ''

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
      >
        🧪
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
