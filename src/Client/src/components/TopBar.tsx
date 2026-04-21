import { PotionSlot } from './PotionSlot'

type Props = {
  currentHp: number
  maxHp: number
  gold: number
  potions: string[]
  onDiscardPotion: (slotIndex: number) => void
}

export function TopBar({ currentHp, maxHp, gold, potions, onDiscardPotion }: Props) {
  return (
    <div className="topbar" role="status">
      <span className="topbar__hp">
        HP {currentHp}/{maxHp}
      </span>
      <span className="topbar__gold">Gold {gold}</span>
      <div className="topbar__potions">
        {potions.map((id, i) => (
          <PotionSlot
            key={i}
            slotIndex={i}
            potionId={id}
            onDiscard={() => onDiscardPotion(i)}
          />
        ))}
      </div>
    </div>
  )
}
