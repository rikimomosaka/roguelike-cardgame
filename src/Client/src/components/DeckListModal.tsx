import { useEffect } from 'react'
import type { CardCatalog } from '../api/catalog'
import { Card } from './Card'
import type { CardType } from './Card'
import { rarityCodeFromNumber } from './rarity'
import './DeckListModal.css'

/**
 * デッキのカード一覧モーダル。キャラクター選択の「初期デッキ」と
 * アーカイブス（キャラクター・モンスター）で共通に使う。
 */
export function DeckListModal({
  title,
  cardIds,
  catalog,
  onClose,
  emptyText = '初期デッキは空です。',
}: {
  title: string
  /** カード ID の並び（重複あり、表示順）。 */
  cardIds: readonly string[]
  catalog: CardCatalog | null
  onClose: () => void
  emptyText?: string
}) {
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  return (
    <div className="character-pick__deck-backdrop" onClick={onClose} role="presentation">
      <div
        className="character-pick__deck-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="deck-list-modal-title"
        onClick={event => event.stopPropagation()}
      >
        <header className="character-pick__deck-modal-header">
          <span id="deck-list-modal-title">{title}</span>
          <button type="button" onClick={onClose} aria-label="閉じる">×</button>
        </header>
        {cardIds.length === 0 ? (
          <p className="character-pick__deck-modal-empty">{emptyText}</p>
        ) : (
          <ul className="character-pick__deck-modal-list">
            {cardIds.map((cardId, index) => {
              const definition = catalog?.[cardId]
              return (
                <li key={`${cardId}-${index}`}>
                  <Card
                    name={definition?.displayName ?? definition?.name ?? cardId}
                    cost={definition?.cost ?? 'X'}
                    costOriginal={definition?.cost ?? null}
                    type={cardTypeCode(definition?.cardType ?? 'Skill')}
                    rarity={rarityCodeFromNumber(definition?.rarity ?? 0)}
                    poolClass={definition?.poolClass ?? null}
                    description={definition?.description ?? ''}
                    upgradedDescription={definition?.upgradedDescription ?? null}
                  />
                </li>
              )
            })}
          </ul>
        )}
      </div>
    </div>
  )
}

function cardTypeCode(value: string): CardType {
  switch (value.toLowerCase()) {
    case 'attack': return 'attack'
    case 'power': return 'power'
    case 'unit': return 'unit'
    case 'curse': return 'curse'
    case 'status': return 'status'
    default: return 'skill'
  }
}
