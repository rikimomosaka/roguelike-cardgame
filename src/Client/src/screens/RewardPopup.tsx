import { useState } from 'react'
import type { RewardStateDto } from '../api/types'
import { Button } from '../components/Button'
import { PotionSlot } from '../components/PotionSlot'

type Props = {
  reward: RewardStateDto
  potions: string[]
  potionSlotCount: number
  onClaimGold: () => Promise<void>
  onClaimPotion: () => Promise<void>
  onPickCard: (cardId: string) => Promise<void>
  onSkipCard: () => Promise<void>
  onProceed: () => Promise<void>
  onDiscardPotion: (slotIndex: number) => Promise<void>
  onPotionFullAlert: () => void
}

export function RewardPopup(p: Props) {
  const [cardView, setCardView] = useState(false)
  const r = p.reward
  const canProceed =
    r.goldClaimed &&
    (r.potionId === null || r.potionClaimed) &&
    r.cardStatus !== 'Pending'

  const handleClaimPotion = async () => {
    try {
      await p.onClaimPotion()
    } catch (e) {
      if ((e as { status?: number }).status === 409) p.onPotionFullAlert()
      else throw e
    }
  }

  if (cardView && r.cardStatus === 'Pending') {
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
              {cid}
            </Button>
          ))}
        </div>
        <Button
          onClick={async () => {
            await p.onSkipCard()
            setCardView(false)
          }}
        >
          Skip
        </Button>
      </div>
    )
  }

  return (
    <div className="reward-popup" role="dialog" aria-modal="true">
      <h2>報酬</h2>
      <ul className="reward-list">
        <li>
          <Button disabled={r.goldClaimed} onClick={() => p.onClaimGold()}>
            {r.goldClaimed ? '✓' : '＋'} {r.gold} Gold
          </Button>
        </li>
        {r.potionId && (
          <li>
            <Button disabled={r.potionClaimed} onClick={handleClaimPotion}>
              {r.potionClaimed ? '✓' : '🧪'} {r.potionId}
            </Button>
          </li>
        )}
        {r.cardChoices.length > 0 && (
          <li>
            <Button
              disabled={r.cardStatus !== 'Pending'}
              onClick={() => setCardView(true)}
            >
              {r.cardStatus !== 'Pending' ? '✓' : '✨'} カードの報酬
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
      <Button disabled={!canProceed} onClick={() => p.onProceed()}>
        進む
      </Button>
    </div>
  )
}
