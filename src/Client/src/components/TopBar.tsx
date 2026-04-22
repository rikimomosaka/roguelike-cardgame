import { useState } from 'react'
import type { CardInstanceDto } from '../api/types'
import { PotionSlot } from './PotionSlot'
import { useCardCatalog } from '../hooks/useCardCatalog'

type Props = {
  currentHp: number
  maxHp: number
  gold: number
  potions: string[]
  deck: CardInstanceDto[]
  onDiscardPotion: (slotIndex: number) => void
  onOpenMenu: () => void
  onTogglePeek?: () => void
  peekActive?: boolean
}

export function TopBar({
  currentHp,
  maxHp,
  gold,
  potions,
  deck,
  onDiscardPotion,
  onOpenMenu,
  onTogglePeek,
  peekActive,
}: Props) {
  const [deckOpen, setDeckOpen] = useState(false)
  const { names } = useCardCatalog()
  const deckLabel = (id: string) => names[id] ?? id
  const sortedDeck = [...deck].sort((a, b) =>
    deckLabel(a.id).localeCompare(deckLabel(b.id), 'ja'),
  )
  const deckOpenAria: 'true' | 'false' = deckOpen ? 'true' : 'false'
  const peekPressedAria: 'true' | 'false' = peekActive ? 'true' : 'false'

  return (
    <div className="topbar" role="status">
      <span className="topbar__hp">
        HP {currentHp}/{maxHp}
      </span>
      <span className="topbar__gold">Gold {gold}</span>
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
            🃏 {deck.length}
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
                  {sortedDeck.map((card, i) => (
                    <li key={`${card.id}-${i}`}>
                      {deckLabel(card.id)}{card.upgraded ? '+' : ''}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          )}
        </div>
        {onTogglePeek && (
          <button
            type="button"
            className="topbar__btn"
            aria-label={peekActive ? '戦闘に戻る' : 'マップを見る'}
            aria-pressed={peekPressedAria}
            onClick={onTogglePeek}
          >
            {peekActive ? '⚔' : '🗺'}
          </button>
        )}
        <button
          type="button"
          className="topbar__btn"
          aria-label="メニュー"
          onClick={onOpenMenu}
        >
          ⚙
        </button>
      </div>
    </div>
  )
}
