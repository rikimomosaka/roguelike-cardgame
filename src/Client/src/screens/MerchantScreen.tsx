import type { CardInstanceDto, MerchantInventoryDto, MerchantOfferDto } from '../api/types'
import { Button } from '../components/Button'
import { useCardCatalog, usePotionCatalog } from '../hooks/useCardCatalog'
import { useRelicCatalog } from '../hooks/useRelicCatalog'

type Props = {
  gold: number
  deck: CardInstanceDto[]
  inventory: MerchantInventoryDto
  onBuy: (kind: 'card' | 'relic' | 'potion', id: string) => void | Promise<void>
  onDiscard: (deckIndex: number) => void | Promise<void>
  onLeave: () => void | Promise<void>
  onClose: () => void
}

export function MerchantScreen(p: Props) {
  const { names: cardNames } = useCardCatalog()
  const { names: relicNames } = useRelicCatalog()
  const { names: potionNames } = usePotionCatalog()

  const left = p.inventory.leftSoFar

  const row = (
    kind: 'card' | 'relic' | 'potion',
    offer: MerchantOfferDto,
    name: string,
  ) => (
    <li key={`${kind}:${offer.id}`} className={`merchant-offer${offer.sold ? ' sold' : ''}`}>
      <span>{name}</span>
      <span>{offer.price} g</span>
      <Button
        onClick={() => p.onBuy(kind, offer.id)}
        disabled={left || offer.sold || p.gold < offer.price}
        aria-label={`Buy ${name}`}
      >
        {offer.sold ? '売切' : '購入'}
      </Button>
    </li>
  )

  return (
    <div className="merchant-screen" role="dialog" aria-modal="true">
      <h2>商人 (Gold: {p.gold})</h2>

      <section>
        <h3>カード</h3>
        <ul>{p.inventory.cards.map(o => row('card', o, cardNames[o.id] ?? o.id))}</ul>
      </section>

      <section>
        <h3>レリック</h3>
        <ul>{p.inventory.relics.map(o => row('relic', o, relicNames[o.id] ?? o.id))}</ul>
      </section>

      <section>
        <h3>ポーション</h3>
        <ul>{p.inventory.potions.map(o => row('potion', o, potionNames[o.id] ?? o.id))}</ul>
      </section>

      <section>
        <h3>除去 ({p.inventory.discardPrice} g、1回のみ)</h3>
        <ul>
          {p.deck.map((c, i) => {
            const name = cardNames[c.id] ?? c.id
            return (
              <li key={i}>
                <span>{name}{c.upgraded ? '+' : ''}</span>
                <Button
                  onClick={() => p.onDiscard(i)}
                  disabled={left || p.inventory.discardSlotUsed || p.gold < p.inventory.discardPrice}
                  aria-label={`Discard ${name} at index ${i}`}
                >
                  除去
                </Button>
              </li>
            )
          })}
        </ul>
      </section>

      {left ? (
        <Button onClick={() => p.onClose()} aria-label="Close">
          閉じる
        </Button>
      ) : (
        <Button onClick={() => p.onLeave()} aria-label="Leave">
          立ち去る
        </Button>
      )}
    </div>
  )
}
