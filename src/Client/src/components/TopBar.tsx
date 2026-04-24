import { useState } from 'react'
import type { CardInstanceDto } from '../api/types'
import { Card } from './Card'
import { cardDisplay } from './cardDisplay'
import { PotionSlot } from './PotionSlot'
import { useCardCatalog } from '../hooks/useCardCatalog'
import { useRelicCatalog } from '../hooks/useRelicCatalog'
import './TopBar.css'

type Props = {
  currentHp: number
  maxHp: number
  gold: number
  potions: string[]
  deck: CardInstanceDto[]
  relics: string[]
  onDiscardPotion: (slotIndex: number) => void
  onOpenMenu: () => void
  onTogglePeek?: () => void
  peekActive?: boolean
  peekDisabled?: boolean
}

export function TopBar({
  currentHp,
  maxHp,
  gold,
  potions,
  deck,
  relics,
  onDiscardPotion,
  onOpenMenu,
  onTogglePeek,
  peekActive,
  peekDisabled,
}: Props) {
  const [deckOpen, setDeckOpen] = useState(false)
  const { names, catalog } = useCardCatalog()
  const { names: relicNames } = useRelicCatalog()
  const deckLabel = (id: string) => names[id] ?? id
  const sortedDeck = [...deck].sort((a, b) =>
    deckLabel(a.id).localeCompare(deckLabel(b.id), 'ja'),
  )
  const deckOpenAria: 'true' | 'false' = deckOpen ? 'true' : 'false'
  const peekPressedAria: 'true' | 'false' = peekActive ? 'true' : 'false'
  const hpPct = Math.max(0, Math.min(100, maxHp > 0 ? (currentHp / maxHp) * 100 : 0))

  return (
    <div className="topbar" role="status">
      <span className="topbar__group topbar__hp">
        <span className="topbar__hp-label">HP {currentHp}/{maxHp}</span>
        <span className="topbar__hp-track" aria-hidden="true">
          <span className="topbar__hp-fill" style={{ width: `${hpPct}%` }} />
        </span>
      </span>
      <span className="topbar__group topbar__gold">
        <span className="topbar__num">{gold}</span> ゴールド
      </span>
      <ul className="topbar__relics" aria-label={`レリック (${relics.length}個)`}>
        {relics.map((id, i) => (
          <li key={`${id}-${i}`} className="topbar__relic" title={relicNames[id] ?? id}>
            <span aria-hidden="true">♆</span> {relicNames[id] ?? id}
          </li>
        ))}
      </ul>
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
      <div className="topbar__actions">
        <div className="topbar__deck-wrap">
          <button
            type="button"
            className="topbar__btn"
            aria-label={`デッキ (${deck.length}枚)`}
            aria-expanded={deckOpenAria}
            aria-pressed={deckOpenAria}
            onClick={() => setDeckOpen((v) => !v)}
          >
            DECK {deck.length}
          </button>
          {deckOpen && (
            <div className="topbar__deck-menu" role="dialog" aria-label="現在のデッキ">
              <header className="topbar__deck-menu-header">
                <span>デッキ ({deck.length}枚)</span>
                <button
                  type="button"
                  className="topbar__btn"
                  aria-label="デッキを閉じる"
                  onClick={() => setDeckOpen(false)}
                >
                  ×
                </button>
              </header>
              {sortedDeck.length === 0 ? (
                <p className="topbar__deck-empty">デッキは空です</p>
              ) : (
                <ul className="topbar__deck-list">
                  {sortedDeck.map((card, i) => {
                    const disp = cardDisplay(card.id, catalog, deckLabel(card.id))
                    return (
                      <li key={`${card.id}-${i}`} className="topbar__deck-item">
                        <Card
                          name={disp.name}
                          cost={disp.cost}
                          type={disp.type}
                          rarity={disp.rarity}
                          upgraded={card.upgraded}
                          width={112}
                        />
                      </li>
                    )
                  })}
                </ul>
              )}
            </div>
          )}
        </div>
        <button
          type="button"
          className="topbar__btn"
          aria-label={peekActive ? '戦闘に戻る' : 'マップを見る'}
          aria-pressed={peekPressedAria}
          onClick={onTogglePeek}
          disabled={peekDisabled || !onTogglePeek}
        >
          {peekActive ? 'BATTLE' : 'MAP'}
        </button>
        <button
          type="button"
          className="topbar__btn"
          aria-label="メニュー"
          onClick={onOpenMenu}
        >
          MENU
        </button>
      </div>
    </div>
  )
}
