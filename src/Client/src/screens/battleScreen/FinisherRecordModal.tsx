import { useEffect, type ReactNode } from 'react'
import type { FinisherRecordDto } from '../../api/types'
import { Card } from '../../components/Card'
import type { CardType } from '../../components/Card'
import { rarityCodeFromName } from '../../components/rarity'
import './FinisherRecordModal.css'

export function FinisherRecordModal({
  record,
  onClose,
  plannedIntent,
}: {
  record: FinisherRecordDto
  onClose: () => void
  plannedIntent?: ReactNode
}) {
  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  return (
    <div className="finisher-record__backdrop" role="presentation" onClick={onClose}>
      <section
        className="finisher-record__modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="finisher-record-title"
        onClick={event => event.stopPropagation()}
      >
        <header className="finisher-record__header">
          <div>
            <h2 id="finisher-record-title">保存された残光</h2>
          </div>
          <button type="button" onClick={onClose} aria-label="閉じる">×</button>
        </header>
        <p className="finisher-record__note">
          {record.afterimageStacks > 0
            ? `通常攻撃後に、以下の${record.cards.length}枚を使用した順に${record.afterimageStacks}回ずつ再実行する（残光${record.afterimageStacks}）。`
            : `残光を得ると、通常攻撃後に、以下の${record.cards.length}枚を使用した順に再実行する。`}
        </p>
        {plannedIntent ? (
          <section className="finisher-record__planned" aria-label="残光の予定アタック値">
            <h3>残光の予定アタック値</h3>
            {plannedIntent}
          </section>
        ) : record.isPlannedAfterimageIntentAvailable === false ? (
          <section className="finisher-record__planned" aria-label="残光の予定アタック値">
            <h3>残光の予定アタック値</h3>
            <p>後続効果により予定値を算出できません。</p>
          </section>
        ) : record.isPlannedAfterimageIntentAvailable === true
          && record.plannedAfterimageIntent === null ? (
          <section className="finisher-record__planned" aria-label="残光の予定アタック値">
            <h3>残光の予定アタック値</h3>
            <p>現在の状態では予定アタックはありません。</p>
          </section>
        ) : null}
        <ol className="finisher-record__cards">
          {record.cards.map((entry, index) => {
            const card = entry.card
            return (
              <li key={`${entry.playSequence}:${card.cardInstanceId}`}>
                <span className="finisher-record__order" aria-hidden="true">
                  {index + 1}
                </span>
                <Card
                  name={card.displayName}
                  cost={card.currentCost}
                  costOrig={card.currentCost !== card.originalCost
                    ? card.originalCost
                    : null}
                  costOriginal={card.originalCost}
                  costChanges={card.costChanges.map(change => ({
                    kind: isCostChangeKind(change.kind) ? change.kind : 'unknown',
                    value: change.value,
                    sourceId: change.sourceId,
                    sourceName: change.sourceName,
                  }))}
                  type={cardType(card.cardType)}
                  rarity={rarityCodeFromName(card.rarity) ?? 'c'}
                  poolClass={card.poolClass ?? null}
                  upgraded={card.isUpgraded}
                  description={card.description}
                />
              </li>
            )
          })}
        </ol>
      </section>
    </div>
  )
}

function cardType(value: string): CardType {
  switch (value.toLowerCase()) {
    case 'attack': return 'attack'
    case 'power': return 'power'
    case 'unit': return 'unit'
    case 'curse': return 'curse'
    case 'status': return 'status'
    default: return 'skill'
  }
}

function isCostChangeKind(
  value: string,
): value is 'fixed' | 'modifier' | 'status' | 'combo' | 'unknown' {
  return value === 'fixed'
    || value === 'modifier'
    || value === 'status'
    || value === 'combo'
    || value === 'unknown'
}
