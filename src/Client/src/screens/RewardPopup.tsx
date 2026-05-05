import { useState } from 'react'
import type { RewardStateDto } from '../api/types'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import { cardDisplay } from '../components/cardDisplay'
import { Popup } from '../components/Popup'
import { PotionSlot } from '../components/PotionSlot'
import { useCardCatalog, usePotionCatalog } from '../hooks/useCardCatalog'
import { useRelicCatalog } from '../hooks/useRelicCatalog'
import './RewardPopup.css'

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
  onRerollCard?: () => Promise<void>  // Phase 10.6.B T7: relic 所持時のみ提供される
}

export function RewardPopup(p: Props) {
  const [cardView, setCardView] = useState(false)
  const r = p.reward
  const cardResolved = r.cardStatus === 'Claimed'
  const { names: cardNames, catalog: cardCatalog } = useCardCatalog()
  const { names: potionNames } = usePotionCatalog()
  const { names: relicNames } = useRelicCatalog()
  const cardLabel = (id: string) => cardNames[id] ?? id
  const potionLabel = (id: string) => potionNames[id] ?? id

  // Reroll は Pending 限定 (server も同条件で 400)。cardResolved (Claimed) のチェックでは
  // soft-skip 後の Skipped 状態を捉えられないため、cardStatus を直接見る。
  const canReroll = r.cardStatus === 'Pending' && !r.rerollUsed && r.rerollAvailable && p.onRerollCard !== undefined

  if (cardView && !cardResolved) {
    return (
      <Popup
        open
        variant="picker"
        title="カードを選ぶ"
        subtitle="1 枚選択"
        width={680}
        footerAlign="center"
        footer={
          <div className="rw-picker__footer-actions">
            {canReroll && (
              <Button
                variant="secondary"
                onClick={async () => {
                  await p.onRerollCard!()
                }}
                aria-label="リロール"
              >
                リロール
              </Button>
            )}
            <Button
              variant="secondary"
              onClick={async () => {
                if (r.cardStatus === 'Pending') await p.onSkipCard()
                setCardView(false)
              }}
              aria-label="閉じる"
            >
              閉じる
            </Button>
          </div>
        }
      >
        <div className="rw-picker">
          {r.cardChoices.map(cid => {
            const disp = cardDisplay(cid, cardCatalog, cardLabel(cid))
            return (
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
                  name={disp.name}
                  cost={disp.cost}
                  type={disp.type}
                  rarity={disp.rarity}
                  description={disp.description}
                  upgradedDescription={disp.upgradedDescription}
                  width={140}
                />
              </button>
            )
          })}
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
      footerAlign="center"
      footer={
        <Button onClick={() => p.onProceed()}>
          {r.isBossReward ? '次の層へ' : '閉じる'}
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
              <span className="rw-tile__icon" aria-hidden="true">
                {r.goldClaimed ? '✓' : <img src="/icons/ui/gold.png" alt="" draggable={false} />}
              </span>
              <span className="rw-num">{r.gold}</span> ゴールド
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
              <span className="rw-tile__icon" aria-hidden="true">
                {r.potionClaimed ? '✓' : <img src={`/icons/potions/${r.potionId}.png`} alt="" draggable={false} />}
              </span>
              {potionLabel(r.potionId)}
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
              <span className="rw-tile__icon" aria-hidden="true">
                {cardResolved ? '✓' : <img src="/icons/ui/reward.png" alt="" draggable={false} />}
              </span>
              カード報酬
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
              <span className="rw-tile__icon" aria-hidden="true">
                {r.relicClaimed ? '✓' : <img src={`/icons/relics/${r.relicId}.png`} alt="" draggable={false} />}
              </span>
              レリック: {relicNames[r.relicId] ?? r.relicId}
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
