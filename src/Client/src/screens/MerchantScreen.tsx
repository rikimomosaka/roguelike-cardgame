import { useMemo, useState } from 'react'
import type { CardInstanceDto, MerchantInventoryDto, MerchantOfferDto } from '../api/types'
import { Button } from '../components/Button'
import { Card } from '../components/Card'
import { cardDisplay } from '../components/cardDisplay'
import { Popup } from '../components/Popup'
import { useTooltipTarget } from '../components/Tooltip'
import type { TooltipContent } from '../components/Tooltip'
import { useCardCatalog, usePotionCatalog } from '../hooks/useCardCatalog'
import { useRelicCatalog } from '../hooks/useRelicCatalog'
import './MerchantScreen.css'

type Props = {
  gold: number
  deck: CardInstanceDto[]
  inventory: MerchantInventoryDto
  onBuy: (kind: 'card' | 'relic' | 'potion', id: string) => void | Promise<void>
  onDiscard: (deckIndex: number) => void | Promise<void>
  onLeave: () => void | Promise<void>
}

export function MerchantScreen(p: Props) {
  const { names: cardNames, catalog: cardCatalog } = useCardCatalog()
  const { names: relicNames, catalog: relicCatalog } = useRelicCatalog()
  const { names: potionNames, catalog: potionCatalog } = usePotionCatalog()
  const [mode, setMode] = useState<'shop' | 'discard'>('shop')
  const [confirming, setConfirming] = useState<{ index: number; name: string } | null>(null)
  const [purchased, setPurchased] = useState(false)

  const canDiscard =
    !p.inventory.discardSlotUsed && p.gold >= p.inventory.discardPrice

  async function handleBuy(kind: 'card' | 'relic' | 'potion', id: string) {
    await p.onBuy(kind, id)
    setPurchased(true)
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
                    disabled={!canDiscard}
                    aria-label={`Discard ${name} at index ${i}`}
                  >
                    <Card
                      name={disp.name}
                      cost={disp.cost}
                      type={disp.type}
                      rarity={disp.rarity}
                      description={disp.description}
                      upgradedDescription={disp.upgradedDescription}
                      upgraded={c.upgraded}
                      width={128}
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
                >
                  いいえ
                </Button>
                <Button
                  variant="danger"
                  onClick={async () => {
                    const idx = confirming.index
                    setConfirming(null)
                    await p.onDiscard(idx)
                    setPurchased(true)
                    setMode('shop')
                  }}
                  aria-label="Confirm discard"
                >
                  はい
                </Button>
              </>
            }
          >
            <p className="mc-confirm-text">
              <strong>{confirming.name}</strong> をデッキから除去します。
            </p>
          </Popup>
        )}
      </>
    )
  }

  const cardSlot = (offer: MerchantOfferDto) => {
    const name = cardNames[offer.id] ?? offer.id
    const disp = cardDisplay(offer.id, cardCatalog, name)
    const locked = !offer.sold && p.gold < offer.price
    const classes = [
      'mc-card-slot',
      offer.sold && 'is-sold',
      locked && 'is-locked',
    ]
      .filter(Boolean)
      .join(' ')
    return (
      <li key={`card:${offer.id}`} className={classes}>
        <Card
          name={disp.name}
          cost={disp.cost}
          type={disp.type}
          rarity={disp.rarity}
          description={disp.description}
          upgradedDescription={disp.upgradedDescription}
          width={128}
        />
        <button
          type="button"
          className="mc-card-slot__buy"
          onClick={() => handleBuy('card', offer.id)}
          disabled={offer.sold || locked}
          aria-label={`Buy ${name}`}
        >
          {offer.sold ? (
            '売切'
          ) : (
            <>
              <span className="mc-num">{offer.price}</span> ゴールド
            </>
          )}
        </button>
      </li>
    )
  }

  return (
    <Popup
      open
      variant="modal"
      title="商人"
      width={820}
      headRight={<span className="mc-gold"><span className="mc-num">{p.gold}</span> ゴールド</span>}
      footerAlign="center"
      footer={
        <Button onClick={() => p.onLeave()} aria-label="Leave">
          立ち去る
        </Button>
      }
    >
      <div className="mc-shopkeeper" aria-hidden="false">
        <div className="mc-shopkeeper__avatar" aria-hidden="true">
          <span className="mc-shopkeeper__sprite">商</span>
        </div>
        <div className="mc-shopkeeper__line" role="status" aria-live="polite">
          <span className="mc-shopkeeper__quote">
            {purchased
              ? 'お買い上げありがとうございます。'
              : 'いらっしゃいませ。'}
          </span>
        </div>
      </div>

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
              icon="◉"
              gold={p.gold}
              onBuy={handleBuy}
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
              icon="⚗"
              gold={p.gold}
              onBuy={handleBuy}
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
                <div className="mc-row__icon" aria-hidden="true">✂</div>
                <div className="mc-row__body">
                  <div className="mc-row__name">カード除去</div>
                </div>
                <div className="mc-row__price"><span className="mc-num">{p.inventory.discardPrice}</span> ゴールド</div>
                <Button
                  onClick={() => setMode('discard')}
                  disabled={!canDiscard}
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
  icon: string
  gold: number
  onBuy: (kind: 'relic' | 'potion', id: string) => void | Promise<void>
}

function RelicPotionTile({ kind, offer, name, description, icon, gold, onBuy }: TileProps) {
  const tooltipContent = useMemo<TooltipContent | null>(() => {
    if (!description) return null
    return { name, desc: description }
  }, [name, description])
  const tip = useTooltipTarget(tooltipContent)
  const locked = !offer.sold && gold < offer.price
  const classes = [
    'mc-tile',
    `mc-tile--${kind}`,
    offer.sold && 'is-sold',
    locked && 'is-locked',
  ]
    .filter(Boolean)
    .join(' ')
  return (
    <li className="mc-tile-wrap">
      <button
        type="button"
        className={classes}
        onClick={() => onBuy(kind, offer.id)}
        disabled={offer.sold || locked}
        aria-label={`Buy ${name}`}
        onMouseEnter={tip.onMouseEnter}
        onMouseMove={tip.onMouseMove}
        onMouseLeave={tip.onMouseLeave}
      >
        <span className="mc-tile__icon" aria-hidden="true">{icon}</span>
        <span className="mc-tile__sr-name">{name}</span>
        <span className="mc-tile__price">
          {offer.sold ? (
            '売切'
          ) : (
            <>
              <span className="mc-num">{offer.price}</span> ゴールド
            </>
          )}
        </span>
      </button>
    </li>
  )
}
