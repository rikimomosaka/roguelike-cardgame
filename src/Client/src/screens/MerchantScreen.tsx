import { useState } from 'react'
import type { CardInstanceDto, MerchantInventoryDto, MerchantOfferDto } from '../api/types'
import { Button } from '../components/Button'
import { Card, type CardRarity, type CardType } from '../components/Card'
import { Popup } from '../components/Popup'
import { useCardCatalog, usePotionCatalog } from '../hooks/useCardCatalog'
import { useRelicCatalog } from '../hooks/useRelicCatalog'
import './MerchantScreen.css'

// Tests run with no catalog loaded → card IDs render raw.
// Neutral defaults keep the Card primitive visually correct.
function inferCardType(_id: string): CardType {
  return 'skill'
}
function inferCardRarity(_id: string): CardRarity {
  return 'c'
}

type Props = {
  gold: number
  deck: CardInstanceDto[]
  inventory: MerchantInventoryDto
  onBuy: (kind: 'card' | 'relic' | 'potion', id: string) => void | Promise<void>
  onDiscard: (deckIndex: number) => void | Promise<void>
  onLeave: () => void | Promise<void>
}

export function MerchantScreen(p: Props) {
  const { names: cardNames } = useCardCatalog()
  const { names: relicNames } = useRelicCatalog()
  const { names: potionNames } = usePotionCatalog()
  const [mode, setMode] = useState<'shop' | 'discard'>('shop')

  const canDiscard =
    !p.inventory.discardSlotUsed && p.gold >= p.inventory.discardPrice

  if (mode === 'discard') {
    return (
      <Popup
        open
        variant="picker"
        title={`カードを除去 (${p.inventory.discardPrice} ゴールド)`}
        subtitle={`デッキから 1 枚`}
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
            return (
              <li key={i} className="mc-picker-item">
                <Card
                  name={name}
                  cost={1}
                  type={inferCardType(c.id)}
                  rarity={inferCardRarity(c.id)}
                  upgraded={c.upgraded}
                  width={128}
                />
                <Button
                  variant="danger"
                  onClick={async () => {
                    await p.onDiscard(i)
                    setMode('shop')
                  }}
                  disabled={!canDiscard}
                  aria-label={`Discard ${name} at index ${i}`}
                >
                  除去
                </Button>
              </li>
            )
          })}
        </ul>
      </Popup>
    )
  }

  const cardSlot = (offer: MerchantOfferDto) => {
    const name = cardNames[offer.id] ?? offer.id
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
          name={name}
          cost={1}
          type={inferCardType(offer.id)}
          rarity={inferCardRarity(offer.id)}
          width={128}
        />
        <button
          type="button"
          className="mc-card-slot__buy"
          onClick={() => p.onBuy('card', offer.id)}
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

  const row = (
    kind: 'relic' | 'potion',
    offer: MerchantOfferDto,
    name: string,
    icon: string,
  ) => {
    const locked = !offer.sold && p.gold < offer.price
    const classes = [
      'mc-row',
      `mc-row--${kind}`,
      offer.sold && 'is-sold',
      locked && 'is-locked',
    ]
      .filter(Boolean)
      .join(' ')
    return (
      <li key={`${kind}:${offer.id}`} className={classes}>
        <div className="mc-row__icon" aria-hidden="true">{icon}</div>
        <div className="mc-row__body">
          <div className="mc-row__name">{name}</div>
        </div>
        <div className="mc-row__price"><span className="mc-num">{offer.price}</span> ゴールド</div>
        <Button
          onClick={() => p.onBuy(kind, offer.id)}
          disabled={offer.sold || locked}
          aria-label={`Buy ${name}`}
        >
          {offer.sold ? '売切' : '購入'}
        </Button>
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
      footer={
        <Button onClick={() => p.onLeave()} aria-label="Leave">
          立ち去る
        </Button>
      }
    >
      <section className="mc-section">
        <div className="mc-section__label">CARDS</div>
        <ul className="mc-cards">
          {p.inventory.cards.map(o => cardSlot(o))}
        </ul>
      </section>

      <section className="mc-section">
        <div className="mc-section__label">RELICS</div>
        <ul className="mc-rows">
          {p.inventory.relics.map(o =>
            row('relic', o, relicNames[o.id] ?? o.id, '◉'),
          )}
        </ul>
      </section>

      <section className="mc-section">
        <div className="mc-section__label">POTIONS</div>
        <ul className="mc-rows">
          {p.inventory.potions.map(o =>
            row('potion', o, potionNames[o.id] ?? o.id, '⚗'),
          )}
        </ul>
      </section>

      <section className="mc-section">
        <div className="mc-section__label">SERVICE</div>
        <ul className="mc-rows">
          <li className="mc-row mc-row--service">
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
        </ul>
      </section>
    </Popup>
  )
}
