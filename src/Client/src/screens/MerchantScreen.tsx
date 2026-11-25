import { useMemo, useRef, useState } from 'react'
import type { CardInstanceDto, MerchantInventoryDto, MerchantOfferDto } from '../api/types'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import type { CardRarity } from '../components/Card'
import { cardDisplay } from '../components/cardDisplay'
import { optionalRarityCodeFromNumber, rarityCodeFromName } from '../components/rarity'
import { Popup } from '../components/Popup'
import { useTooltipTarget } from '../components/Tooltip'
import type { TooltipContent } from '../components/Tooltip'
import { useCardCatalog, usePotionCatalog } from '../hooks/useCardCatalog'
import { useRelicCatalog } from '../hooks/useRelicCatalog'
import { getRelicTooltipContent } from '../components/relicTooltip'
import { potionIcon, relicIcon, systemIcon } from '../presentation/assets'
import './MerchantScreen.css'

type Props = {
  gold: number
  deck: CardInstanceDto[]
  inventory: MerchantInventoryDto
  onBuy: (kind: 'card' | 'relic' | 'potion', id: string) => void | Promise<void>
  onDiscard: (deckIndex: number) => void | Promise<void>
  onLeave: () => void | Promise<void>
}

type SelectedOffer = {
  kind: 'card' | 'relic' | 'potion'
  id: string
  name: string
  price: number
}

export function MerchantScreen(p: Props) {
  const { names: cardNames, catalog: cardCatalog } = useCardCatalog()
  const { names: relicNames, catalog: relicCatalog } = useRelicCatalog()
  const { names: potionNames, catalog: potionCatalog } = usePotionCatalog()
  const [mode, setMode] = useState<'shop' | 'discard'>('shop')
  const [confirming, setConfirming] = useState<{ index: number; name: string } | null>(null)
  const [purchased, setPurchased] = useState(false)
  const [selectedOffer, setSelectedOffer] = useState<SelectedOffer | null>(null)
  const [pendingAction, setPendingAction] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const pendingActionRef = useRef<string | null>(null)

  const canDiscard =
    !p.inventory.discardSlotUsed && p.gold >= p.inventory.discardPrice

  function beginAction(action: string) {
    if (pendingActionRef.current !== null) return false
    pendingActionRef.current = action
    setPendingAction(action)
    return true
  }

  function endAction() {
    pendingActionRef.current = null
    setPendingAction(null)
  }

  async function handleBuy(kind: 'card' | 'relic' | 'potion', id: string) {
    if (!beginAction(`${kind}:${id}`)) return
    setActionError(null)
    try {
      await p.onBuy(kind, id)
      setPurchased(true)
      setSelectedOffer(null)
    } catch {
      setActionError('取引に失敗しました。もう一度お試しください。')
    } finally {
      endAction()
    }
  }

  if (mode === 'discard') {
    return (
      <>
        <Popup
          open
          variant="picker"
          title={`カードを除去 (${p.inventory.discardPrice} ゴールド)`}
          subtitle={`カードをクリックして除去`}
          width={760}
          footer={
            <Button
              variant="secondary"
              onClick={() => setMode('shop')}
              aria-label="Back"
              disabled={pendingAction !== null}
            >
              戻る
            </Button>
          }
        >
          <ul className="mc-picker-body">
            {p.deck.map((c, i) => {
              const name = cardNames[c.id] ?? c.id
              const disp = cardDisplay(c.id, cardCatalog, name)
              return (
                <li key={i} className="mc-picker-item">
                  <button
                    type="button"
                    className="mc-picker-card"
                    onClick={() => setConfirming({ index: i, name })}
                    disabled={!canDiscard || pendingAction !== null}
                    aria-label={`Discard ${name} at index ${i}`}
                  >
                    <Card
                      cardId={c.id}
                      name={disp.name}
                      cost={disp.cost}
                      type={disp.type}
                      rarity={disp.rarity}
                      poolClass={disp.poolClass}
                      description={disp.description}
                      upgradedDescription={disp.upgradedDescription}
                      upgraded={c.upgraded}
                    />
                    <span className="mc-picker-card__overlay" aria-hidden="true">
                      <span className="mc-picker-card__overlay-label">削除</span>
                    </span>
                  </button>
                </li>
              )
            })}
          </ul>
        </Popup>
        {confirming && (
          <Popup
            open
            variant="confirm"
            title="削除しますか?"
            width={420}
            footer={
              <>
                <Button
                  variant="secondary"
                  onClick={() => setConfirming(null)}
                  aria-label="Cancel discard"
                  disabled={pendingAction !== null}
                >
                  いいえ
                </Button>
                <Button
                  variant="danger"
                  onClick={async () => {
                    const idx = confirming.index
                    if (!beginAction(`discard:${idx}`)) return
                    setActionError(null)
                    try {
                      await p.onDiscard(idx)
                      setPurchased(true)
                      setConfirming(null)
                      setMode('shop')
                    } catch {
                      setActionError('カードを削除できませんでした。')
                    } finally {
                      endAction()
                    }
                  }}
                  aria-label="Confirm discard"
                  disabled={pendingAction !== null}
                >
                  {pendingAction?.startsWith('discard:') ? '削除中…' : 'はい'}
                </Button>
              </>
            }
          >
            <p className="mc-confirm-text">
              <strong>{confirming.name}</strong> をデッキから除去します。
            </p>
            {actionError && (
              <p className="mc-action-error" role="alert">{actionError}</p>
            )}
          </Popup>
        )}
      </>
    )
  }

  const cardSlot = (offer: MerchantOfferDto) => {
    const name = cardNames[offer.id] ?? offer.id
    if (offer.sold) {
      // レリック / ポーションの売切タイルと同じく、カード絵は消して売切プレースホルダを出す
      return (
        <li key={`card:${offer.id}`} className="mc-card-slot is-sold">
          <div className="mc-card-slot__placeholder" aria-label={`${name} (売切)`}>
            <span className="mc-card-slot__placeholder-body">
              <span className="mc-card-slot__placeholder-mark" aria-hidden="true">売切</span>
            </span>
            <span className="mc-card-slot__price mc-card-slot__price--sold">売切</span>
          </div>
        </li>
      )
    }
    const disp = cardDisplay(offer.id, cardCatalog, name)
    const locked = p.gold < offer.price
    const selected = selectedOffer?.kind === 'card' && selectedOffer.id === offer.id
    const classes = ['mc-card-slot', locked && 'is-locked', selected && 'is-selected'].filter(Boolean).join(' ')
    return (
      <li key={`card:${offer.id}`} className={classes}>
        <button
          type="button"
          className="mc-card-slot__btn"
          onClick={() => setSelectedOffer({
            kind: 'card',
            id: offer.id,
            name,
            price: offer.price,
          })}
          disabled={pendingAction !== null}
          aria-pressed={selected}
          aria-label={`Select ${name}`}
        >
          <Card
            cardId={offer.id}
            name={disp.name}
            cost={disp.cost}
            type={disp.type}
            rarity={disp.rarity}
            poolClass={disp.poolClass}
            description={disp.description}
            upgradedDescription={disp.upgradedDescription}
          />
          <span className="mc-card-slot__price">
            <span className="mc-num">{offer.price}</span> ゴールド
          </span>
        </button>
      </li>
    )
  }

  return (
    <Popup
      open
      variant="modal"
      title="商店"
      width={820}
      panelClassName="mc-shop-panel"
      headRight={<span className="mc-gold">所持 <span className="mc-num">{p.gold}</span> ゴールド</span>}
      footerAlign="center"
      footer={
        <Button
          onClick={async () => {
            if (!beginAction('leave')) return
            try { await p.onLeave() } finally { endAction() }
          }}
          aria-label="Leave"
          disabled={pendingAction !== null}
        >
          {pendingAction === 'leave' ? '退出中…' : '立ち去る'}
        </Button>
      }
    >
      <section className="mc-shopkeeper" aria-label="商人との取引">
        <div className="mc-shopkeeper__avatar" aria-hidden="true">
          <span className="mc-shopkeeper__sprite">
            <img src={systemIcon('merchant')} alt="" draggable={false} />
          </span>
        </div>
        <div className="mc-shopkeeper__line">
          <div className="mc-shopkeeper__dialogue">
            <span className="mc-shopkeeper__name">商人</span>
            <span
              className="mc-shopkeeper__quote"
              role="status"
              aria-live="polite"
              aria-atomic="true"
            >
              {selectedOffer
                ? 'こちらをお買い上げになりますか？'
                : purchased
                  ? 'ありがとうございます。'
                  : 'いらっしゃいませ。'}
            </span>
          </div>
          {selectedOffer && (
            <div
              className="mc-shopkeeper__purchase"
              role="group"
              aria-label={`${selectedOffer.name}の購入確認`}
            >
              <span
                className="mc-shopkeeper__price"
                aria-label={`価格 ${selectedOffer.price}ゴールド`}
              >
                <span className="mc-num">{selectedOffer.price}</span>
                <span aria-hidden="true"> ゴールド</span>
              </span>
              <Button
                onClick={() => void handleBuy(selectedOffer.kind, selectedOffer.id)}
                disabled={pendingAction !== null || p.gold < selectedOffer.price}
                aria-label={`Buy ${selectedOffer.name}`}
              >
                {pendingAction === `${selectedOffer.kind}:${selectedOffer.id}`
                  ? '購入中…'
                  : p.gold < selectedOffer.price ? 'ゴールド不足' : '購入'}
              </Button>
            </div>
          )}
        </div>
      </section>

      <div className="mc-live-region" aria-live="polite" aria-atomic="true">
        {pendingAction && pendingAction !== 'leave' ? '取引を処理しています…' : ''}
      </div>
      {actionError && <p className="mc-action-error" role="alert">{actionError}</p>}

      <section className="mc-section">
        <div className="mc-section__label">CARDS</div>
        <ul className="mc-cards">
          {p.inventory.cards.map(o => cardSlot(o))}
        </ul>
      </section>

      <section className="mc-section">
        <div className="mc-section__label">RELICS</div>
        <ul className="mc-tile-grid">
          {p.inventory.relics.map(o => (
            <RelicPotionTile
              key={`relic:${o.id}`}
              kind="relic"
              offer={o}
              name={relicNames[o.id] ?? o.id}
              description={relicCatalog?.[o.id]?.description ?? null}
              rarity={rarityCodeFromName(relicCatalog?.[o.id]?.rarity)}
              relicTooltipContent={getRelicTooltipContent(o.id, relicCatalog, relicNames)}
              gold={p.gold}
              selected={selectedOffer?.kind === 'relic' && selectedOffer.id === o.id}
              onSelect={(offer) => setSelectedOffer(offer)}
              disabled={pendingAction !== null}
            />
          ))}
        </ul>
      </section>

      <section className="mc-section">
        <div className="mc-section__label">POTIONS</div>
        <ul className="mc-tile-grid">
          {p.inventory.potions.map(o => (
            <RelicPotionTile
              key={`potion:${o.id}`}
              kind="potion"
              offer={o}
              name={potionNames[o.id] ?? o.id}
              description={potionCatalog?.[o.id]?.description ?? null}
              rarity={optionalRarityCodeFromNumber(potionCatalog?.[o.id]?.rarity)}
              gold={p.gold}
              selected={selectedOffer?.kind === 'potion' && selectedOffer.id === o.id}
              onSelect={(offer) => setSelectedOffer(offer)}
              disabled={pendingAction !== null}
            />
          ))}
        </ul>
      </section>

      <section className="mc-section">
        <div className="mc-section__label">SERVICE</div>
        <ul className="mc-rows">
          {(() => {
            const locked = !p.inventory.discardSlotUsed && p.gold < p.inventory.discardPrice
            const classes = [
              'mc-row',
              'mc-row--service',
              p.inventory.discardSlotUsed && 'is-sold',
              locked && 'is-locked',
            ].filter(Boolean).join(' ')
            return (
              <li className={classes}>
                <div className="mc-row__icon" aria-hidden="true">
                  <img src={systemIcon('discard')} alt="" draggable={false} />
                </div>
                <div className="mc-row__body">
                  <div className="mc-row__name">カード除去</div>
                </div>
                <div className="mc-row__price"><span className="mc-num">{p.inventory.discardPrice}</span> ゴールド</div>
                <Button
                  onClick={() => setMode('discard')}
                  disabled={!canDiscard || pendingAction !== null}
                  aria-label="Open discard view"
                >
                  {p.inventory.discardSlotUsed ? '売切' : '除去'}
                </Button>
              </li>
            )
          })()}
        </ul>
      </section>
    </Popup>
  )
}

type TileProps = {
  kind: 'relic' | 'potion'
  offer: MerchantOfferDto
  name: string
  description: string | null
  rarity?: CardRarity
  relicTooltipContent?: TooltipContent | null
  gold: number
  selected: boolean
  disabled: boolean
  onSelect: (offer: SelectedOffer) => void
}

function RelicPotionTile({ kind, offer, name, description, rarity, relicTooltipContent, gold, selected, disabled, onSelect }: TileProps) {
  const iconSrc = kind === 'relic' ? relicIcon(offer.id) : potionIcon(offer.id)
  const fallbackTooltipContent = useMemo<TooltipContent | null>(() => {
    if (offer.sold || !description) return null
    return { name, rarity, desc: description }
  }, [name, rarity, description, offer.sold])
  const tooltipContent = relicTooltipContent ?? fallbackTooltipContent
  const tip = useTooltipTarget(tooltipContent)
  const locked = !offer.sold && gold < offer.price
  const classes = [
    'mc-tile',
    `mc-tile--${kind}`,
    offer.sold && 'is-sold',
    locked && 'is-locked',
    selected && 'is-selected',
  ]
    .filter(Boolean)
    .join(' ')

  if (offer.sold) {
    return (
      <li className="mc-tile-wrap">
        <div className={classes} aria-label={`${name} (売切)`}>
          <span className="mc-tile__sr-name">{name}</span>
          <span className="mc-tile__price mc-tile__price--sold">売切</span>
        </div>
      </li>
    )
  }

  return (
    <li
      className="mc-tile-wrap"
      onMouseEnter={tip.onMouseEnter}
      onMouseMove={tip.onMouseMove}
      onMouseLeave={tip.onMouseLeave}
    >
      <button
        type="button"
        className={classes}
        onClick={() => onSelect({
          kind,
          id: offer.id,
          name,
          price: offer.price,
        })}
        disabled={disabled}
        aria-pressed={selected}
        aria-label={`Select ${name}`}
      >
        <span className="mc-tile__icon" aria-hidden="true">
          <img src={iconSrc} alt="" draggable={false} />
        </span>
        <span className="mc-tile__sr-name">{name}</span>
        <span className="mc-tile__price">
          <span className="mc-num">{offer.price}</span> ゴールド
        </span>
      </button>
    </li>
  )
}
