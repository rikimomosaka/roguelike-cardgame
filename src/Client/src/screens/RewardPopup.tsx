import { useState } from 'react'
import type { RewardStateDto } from '../api/types'
import { Button } from '../components/Button'
import { Card, type CardRarity, type CardType } from '../components/Card'
import { Popup } from '../components/Popup'
import { PotionSlot } from '../components/PotionSlot'
import { useCardCatalog, usePotionCatalog } from '../hooks/useCardCatalog'
import { useRelicCatalog } from '../hooks/useRelicCatalog'
import './RewardPopup.css'

// The card catalog does not yet expose per-card type/rarity in all environments
// (tests run with no catalog loaded → card IDs render raw). Neutral defaults
// keep the Card primitive visually correct.
function inferType(_id: string): CardType {
  return 'skill'
}
function inferRarity(_id: string): CardRarity {
  return 'c'
}

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
      <Popup
        open
        variant="picker"
        title="カードを選ぶ"
        subtitle={`${r.cardChoices.length} 枚から 1 枚`}
        width={680}
        footer={
          <Button
            onClick={async () => {
              if (r.cardStatus === 'Pending') await p.onSkipCard()
              setCardView(false)
            }}
          >
            Skip
          </Button>
        }
      >
        <div className="rw-picker">
          {r.cardChoices.map(cid => (
            <button
              key={cid}
              type="button"
              className="rw-picker__card"
              onClick={async () => {
                await p.onPickCard(cid)
                setCardView(false)
              }}
              aria-label={cardLabel(cid)}
            >
              <Card
                name={cardLabel(cid)}
                cost={1}
                type={inferType(cid)}
                rarity={inferRarity(cid)}
                width={140}
              />
            </button>
          ))}
        </div>
      </Popup>
    )
  }

  return (
    <Popup
      open
      variant="modal"
      title="報酬"
      width={620}
      footer={
        <Button onClick={() => p.onProceed()}>
          {r.isBossReward ? '次の層へ' : '進む'}
        </Button>
      }
    >
      <ul className="rw-list">
        {r.gold > 0 && (
          <li className="rw-row rw-row--gold">
            <button
              type="button"
              className="rw-tile"
              disabled={r.goldClaimed}
              onClick={() => p.onClaimGold()}
            >
              {r.goldClaimed ? '✓' : '＋'} {r.gold} Gold
            </button>
          </li>
        )}
        {r.potionId && (
          <li className="rw-row rw-row--potion">
            <button
              type="button"
              className="rw-tile"
              disabled={r.potionClaimed}
              onClick={() => p.onClaimPotion()}
            >
              {r.potionClaimed ? '✓' : '🧪'} {potionLabel(r.potionId)}
            </button>
          </li>
        )}
        {r.cardChoices.length > 0 && (
          <li className="rw-row rw-row--card">
            <button
              type="button"
              className="rw-tile"
              disabled={cardResolved}
              onClick={() => setCardView(true)}
            >
              {cardResolved ? '✓' : '✨'} カードの報酬
            </button>
          </li>
        )}
        {r.relicId && (
          <li className="rw-row rw-row--relic">
            <button
              type="button"
              className="rw-tile"
              disabled={r.relicClaimed}
              onClick={() => p.onClaimRelic()}
            >
              {r.relicClaimed ? '✓' : '💎'} レリック: {relicNames[r.relicId] ?? r.relicId}
            </button>
          </li>
        )}
      </ul>
      {p.potions.length > 0 && (
        <div className="rw-potion-slots">
          {p.potions.map((id, i) => (
            <PotionSlot
              key={i}
              slotIndex={i}
              potionId={id}
              onDiscard={() => p.onDiscardPotion(i)}
            />
          ))}
        </div>
      )}
    </Popup>
  )
}
