import { useEffect, type ReactNode } from 'react'
import type { FinisherRecordCardDto, FinisherRecordDto } from '../../api/types'
import { Card } from '../../components/Card'
import type { CardType } from '../../components/Card'
import { rarityCodeFromName } from '../../components/rarity'
import './FinisherRecordModal.css'

/**
 * このターンのプレイ履歴。どのクラスでも使う履歴確認用のウインドウで、
 * 残光が保存されている時だけ、保存中のカードに再実行順の番号を付けて強調する。
 */
export function PlayHistoryModal({
  history,
  record,
  onClose,
  plannedIntent,
}: {
  history: readonly FinisherRecordCardDto[]
  record: FinisherRecordDto | null
  onClose: () => void
  plannedIntent?: ReactNode
}) {
  useEffect(() => {
    // 捕捉段階で処理し、マップ画面の「ESC でメニュー」に伝わらないようにする。
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return
      event.preventDefault()
      event.stopImmediatePropagation()
      onClose()
    }
    window.addEventListener('keydown', handleKeyDown, true)
    return () => window.removeEventListener('keydown', handleKeyDown, true)
  }, [onClose])

  const savedCards = record?.cards ?? []
  const savedOrder = new Map(savedCards.map((entry, index) => [entry.playSequence, index + 1]))
  // 連環でターンを跨いだ保存カードなど、今ターンの履歴に無い保存分も表示する。
  const historySequences = new Set(history.map(entry => entry.playSequence))
  const entries = [
    ...savedCards.filter(entry => !historySequences.has(entry.playSequence)),
    ...history,
  ]

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
            <h2 id="finisher-record-title">このターンのプレイ履歴</h2>
          </div>
          <button type="button" onClick={onClose} aria-label="閉じる">×</button>
        </header>
        {savedCards.length > 0 ? (
          <p className="finisher-record__note">
            {record!.afterimageStacks > 0
              ? `攻撃終了時に、番号の付いた${savedCards.length}枚を番号順に${record!.afterimageStacks}回ずつ再実行する（残光${record!.afterimageStacks}）。`
              : `残光を得ると、攻撃終了時に番号の付いた${savedCards.length}枚を番号順に再実行する。`}
          </p>
        ) : null}
        {plannedIntent ? (
          <section className="finisher-record__planned" aria-label="残光の予定アタック値">
            <h3>残光の予定アタック値</h3>
            {plannedIntent}
          </section>
        ) : record?.isPlannedAfterimageIntentAvailable === false ? (
          <section className="finisher-record__planned" aria-label="残光の予定アタック値">
            <h3>残光の予定アタック値</h3>
            <p>後続効果により予定値を算出できません。</p>
          </section>
        ) : record?.isPlannedAfterimageIntentAvailable === true
          && record.plannedAfterimageIntent === null ? (
          <section className="finisher-record__planned" aria-label="残光の予定アタック値">
            <h3>残光の予定アタック値</h3>
            <p>現在の状態では予定アタックはありません。</p>
          </section>
        ) : null}
        {entries.length === 0 ? (
          <p className="finisher-record__empty">このターンはまだカードを使っていません。</p>
        ) : (
          <ol className="finisher-record__cards">
            {entries.map(entry => {
              const card = entry.card
              const order = savedOrder.get(entry.playSequence)
              return (
                <li
                  key={`${entry.playSequence}:${card.cardInstanceId}`}
                  className={order ? 'is-afterimage-saved' : undefined}
                  data-afterimage-order={order}
                >
                  {order ? (
                    <span className="finisher-record__order" aria-label={`残光の再実行順 ${order}`}>
                      {order}
                    </span>
                  ) : null}
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
        )}
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
