import { useState } from 'react'
import type { RewardStateDto } from '../api/types'
import { Button } from '../components/Button'
import { PotionSlot } from '../components/PotionSlot'
import { useCardCatalog, usePotionCatalog } from '../hooks/useCardCatalog'
import { useRelicCatalog } from '../hooks/useRelicCatalog'

type Props = {
  reward: RewardStateDto
  potions: string[]
  potionSlotCount: number
  onClaimGold: () => Promise<void>
  onClaimPotion: () => Promise<void>
  onPickCard: (cardId: string) => Promise<void>
  onSkipCard: () => Promise<void>
  onProceed: () => void
  onDiscardPotion: (slotIndex: number) => Promise<void>
  onClaimRelic: () => Promise<void>
}

export function RewardPopup(p: Props) {
  const [cardView, setCardView] = useState(false)
  const r = p.reward
  const cardResolved = r.cardStatus === 'Claimed'
  const { names: cardNames } = useCardCatalog()
  const { names: potionNames } = usePotionCatalog()
  const { names: relicNames } = useRelicCatalog()
  const cardLabel = (id: string) => cardNames[id] ?? id
  const potionLabel = (id: string) => potionNames[id] ?? id

  if (cardView && !cardResolved) {
    return (
      <div className="reward-popup" role="dialog" aria-modal="true">
        <h2>カードを選ぶ</h2>
        <div className="reward-card-choices">
          {r.cardChoices.map(cid => (
            <Button
              key={cid}
              onClick={async () => {
                await p.onPickCard(cid)
                setCardView(false)
              }}
            >
              {cardLabel(cid)}
            </Button>
          ))}
        </div>
        {r.cardStatus === 'Pending' && (
          <Button
            onClick={async () => {
              await p.onSkipCard()
              setCardView(false)
            }}
          >
            Skip
          </Button>
        )}
        <Button onClick={() => setCardView(false)}>戻る</Button>
      </div>
    )
  }

  return (
    <div className="reward-popup" role="dialog" aria-modal="true">
      <h2>報酬</h2>
      <ul className="reward-list">
        {r.gold > 0 && (
          <li>
            <Button disabled={r.goldClaimed} onClick={() => p.onClaimGold()}>
              {r.goldClaimed ? '✓' : '＋'} {r.gold} Gold
            </Button>
          </li>
        )}
        {r.potionId && (
          <li>
            <Button disabled={r.potionClaimed} onClick={() => p.onClaimPotion()}>
              {r.potionClaimed ? '✓' : '🧪'} {potionLabel(r.potionId)}
            </Button>
          </li>
        )}
        {r.cardChoices.length > 0 && (
          <li>
            <Button
              disabled={cardResolved}
              onClick={() => setCardView(true)}
            >
              {cardResolved ? '✓' : '✨'} カードの報酬
            </Button>
          </li>
        )}
        {r.relicId && (
          <li>
            <Button
              disabled={r.relicClaimed}
              onClick={() => p.onClaimRelic()}
            >
              {r.relicClaimed ? '✓' : '💎'} レリック: {relicNames[r.relicId] ?? r.relicId}
            </Button>
          </li>
        )}
      </ul>
      <div className="reward-popup__potion-slots">
        {p.potions.map((id, i) => (
          <PotionSlot
            key={i}
            slotIndex={i}
            potionId={id}
            onDiscard={() => p.onDiscardPotion(i)}
          />
        ))}
      </div>
      <Button onClick={() => p.onProceed()}>{r.isBossReward ? '次の層へ' : '進む'}</Button>
    </div>
  )
}
