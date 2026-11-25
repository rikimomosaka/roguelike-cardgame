import { useMemo, useState } from 'react'
import type { RewardStateDto } from '../api/types'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import { cardDisplay } from '../components/cardDisplay'
import { Popup } from '../components/Popup'
import { PotionSlot } from '../components/PotionSlot'
import { getRelicTooltipContent } from '../components/relicTooltip'
import { useTooltipTarget } from '../components/Tooltip'
import { rarityCodeFromNumber } from '../components/rarity'
import type { TooltipContent } from '../components/Tooltip'
import { useCardCatalog, usePotionCatalog } from '../hooks/useCardCatalog'
import { useRelicCatalog } from '../hooks/useRelicCatalog'
import { potionIcon, relicIcon, systemIcon } from '../presentation/assets'
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
  const [pending, setPending] = useState<string | null>(null)
  const [justClaimed, setJustClaimed] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const r = p.reward
  const cardResolved = r.cardStatus === 'Claimed'
  const { names: cardNames, catalog: cardCatalog } = useCardCatalog()
  const { names: potionNames, catalog: potionCatalog } = usePotionCatalog()
  const { names: relicNames, catalog: relicCatalog } = useRelicCatalog()
  const cardLabel = (id: string) => cardNames[id] ?? id
  const potionLabel = (id: string) => potionNames[id] ?? id
  const relicTooltipContent = useMemo(
    () => r.relicId
      ? getRelicTooltipContent(r.relicId, relicCatalog, relicNames)
      : null,
    [r.relicId, relicCatalog, relicNames],
  )
  const relicTip = useTooltipTarget(relicTooltipContent)
  const potionTooltipContent = useMemo<TooltipContent | null>(
    () => {
      if (!r.potionId) return null
      const entry = potionCatalog?.[r.potionId]
      return {
        name: entry?.name ?? r.potionId,
        rarity: entry !== undefined ? rarityCodeFromNumber(entry.rarity) : undefined,
        desc: entry?.description ?? 'ポーションの効果情報はありません。',
      }
    },
    [r.potionId, potionCatalog],
  )
  const potionTip = useTooltipTarget(potionTooltipContent)

  async function runAction(key: string, action: () => void | Promise<void>): Promise<boolean> {
    if (pending !== null) return false
    setActionError(null)
    setJustClaimed(null)
    setPending(key)
    try {
      await action()
      setJustClaimed(key)
      return true
    } catch {
      setActionError('報酬を受け取れませんでした。もう一度お試しください。')
      return false
    } finally {
      setPending(null)
    }
  }

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
                  await runAction('reroll', p.onRerollCard!)
                }}
                aria-label="リロール"
                disabled={pending !== null}
              >
                {pending === 'reroll' ? '再抽選中…' : 'リロール'}
              </Button>
            )}
            <Button
              variant="secondary"
              onClick={async () => {
                const succeeded = r.cardStatus !== 'Pending'
                  || await runAction('skip-card', p.onSkipCard)
                if (succeeded) setCardView(false)
              }}
              aria-label="閉じる"
              disabled={pending !== null}
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
                  const succeeded = await runAction(`card:${cid}`, () => p.onPickCard(cid))
                  if (succeeded) setCardView(false)
                }}
                aria-label={cardLabel(cid)}
                disabled={pending !== null}
              >
                <Card
                  name={disp.name}
                  cost={disp.cost}
                  type={disp.type}
                  rarity={disp.rarity}
                  poolClass={disp.poolClass}
                  description={disp.description}
                  upgradedDescription={disp.upgradedDescription}
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
        <Button
          onClick={() => void runAction('proceed', p.onProceed)}
          disabled={pending !== null}
        >
          {r.isBossReward ? '次の層へ' : '閉じる'}
        </Button>
      }
    >
      <div className="rw-feedback" aria-live="polite" aria-atomic="true">
        {pending && <span role="status">報酬を受け取っています…</span>}
        {!pending && justClaimed && <span>受け取りました</span>}
      </div>
      {actionError && <p className="rw-error" role="alert">{actionError}</p>}
      <ul className="rw-list" aria-busy={pending !== null}>
        {r.gold > 0 && (
          <li className={`rw-row rw-row--gold${justClaimed === 'gold' ? ' is-claimed-now' : ''}`}>
            <button
              type="button"
              className="rw-tile"
              disabled={r.goldClaimed || pending !== null}
              onClick={() => void runAction('gold', p.onClaimGold)}
            >
              <span className="rw-tile__icon" aria-hidden="true">
                {r.goldClaimed ? '✓' : <img src={systemIcon('gold')} alt="" draggable={false} />}
              </span>
              <span className="rw-num">{r.gold}</span> ゴールド
            </button>
          </li>
        )}
        {r.potionId && (
          <li className={`rw-row rw-row--potion${justClaimed === 'potion' ? ' is-claimed-now' : ''}`} {...potionTip}>
            <button
              type="button"
              className="rw-tile"
              disabled={r.potionClaimed || pending !== null}
              onClick={() => void runAction('potion', p.onClaimPotion)}
            >
              <span className="rw-tile__icon" aria-hidden="true">
                {r.potionClaimed ? '✓' : <img src={potionIcon(r.potionId)} alt="" draggable={false} />}
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
              disabled={cardResolved || pending !== null}
              onClick={() => setCardView(true)}
            >
              <span className="rw-tile__icon" aria-hidden="true">
                {cardResolved ? '✓' : <img src={systemIcon('reward')} alt="" draggable={false} />}
              </span>
              カード報酬
            </button>
          </li>
        )}
        {r.relicId && (
          <li className={`rw-row rw-row--relic${justClaimed === 'relic' ? ' is-claimed-now' : ''}`}>
            <button
              type="button"
              className="rw-tile"
              disabled={r.relicClaimed || pending !== null}
              onClick={() => void runAction('relic', p.onClaimRelic)}
              {...relicTip}
            >
              <span className="rw-tile__icon" aria-hidden="true">
                {r.relicClaimed ? '✓' : <img src={relicIcon(r.relicId)} alt="" draggable={false} />}
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
