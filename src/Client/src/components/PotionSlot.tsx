import { useState } from 'react'

type Props = {
  slotIndex: number
  potionId: string
  onDiscard: () => void
}

export function PotionSlot({ slotIndex, potionId, onDiscard }: Props) {
  const [menuOpen, setMenuOpen] = useState(false)
  const filled = potionId !== ''

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
      <button className="potion-slot__icon" onClick={() => setMenuOpen(v => !v)}>
        🧪
      </button>
      {menuOpen && (
        <div className="potion-slot__menu" role="menu">
          <button
            onClick={() => {
              onDiscard()
              setMenuOpen(false)
            }}
          >
            捨てる
          </button>
          <button onClick={() => setMenuOpen(false)}>キャンセル</button>
        </div>
      )}
    </div>
  )
}
