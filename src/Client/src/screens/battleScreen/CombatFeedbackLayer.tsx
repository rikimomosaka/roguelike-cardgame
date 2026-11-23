import type { CSSProperties } from 'react'
import './CombatFeedbackLayer.css'

export type CombatFeedbackKind =
  | 'damage'
  | 'attack'
  | 'block'
  | 'guard'
  | 'heal'
  | 'buff'
  | 'debuff'

export type CombatFeedbackItem = {
  id: number
  targetId: string
  kind: CombatFeedbackKind
  text: string
}

export function CombatFeedbackStack({
  items,
}: {
  items: readonly CombatFeedbackItem[]
}) {
  if (items.length === 0) return null
  return (
    <div className="combat-feedback" aria-live="polite" aria-atomic="false">
      {items.map((item, index) => (
        <span
          key={item.id}
          className={`combat-feedback__item combat-feedback__item--${item.kind}`}
          style={{ '--feedback-index': index } as CSSProperties}
        >
          {item.text}
        </span>
      ))}
    </div>
  )
}
