import type { RunResultDto } from '../api/types'
import './RunResultScreen.css'

type Props = {
  result: RunResultDto
  onReturnToMenu: () => void
}

function formatSeconds(total: number): string {
  const h = Math.floor(total / 3600)
  const m = Math.floor((total % 3600) / 60)
  const s = total % 60
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}`
}

function outcomeClass(outcome: string): string {
  switch (outcome) {
    case 'Cleared':
      return 'rr__outcome--cleared'
    case 'GameOver':
      return 'rr__outcome--gameover'
    case 'Abandoned':
    default:
      return 'rr__outcome--abandoned'
  }
}

export function RunResultScreen({ result, onReturnToMenu }: Props) {
  const relicCount = result.finalRelics.length
  const deckCount = result.finalDeck.length

  return (
    <main className="rr" role="dialog" aria-modal="true" aria-label="Run Result">
      <div className="rr__pattern" aria-hidden="true" />
      <div className="rr__content">

        {/* ---- Header ---- */}
        <header className="rr__header">
          <h1 className={`rr__outcome ${outcomeClass(result.outcome)}`}>
            {result.outcome}
          </h1>
          <div className="rr__ornament" aria-hidden="true">❖ ❖ ❖</div>
        </header>

        {/* ---- Statistics panel ---- */}
        <section className="rr__panel rr__stats-panel" aria-label="Statistics">
          <div className="rr__panel-title-row">
            <div className="rr__panel-title">▸ STATISTICS</div>
          </div>

          <div className="rr__stats-body">
            <dl className="rr__stats-grid">
              <div className="rr__stat-line">
                <dt className="rr__stat-label">到達層</dt>
                <dd className="rr__stat-value">
                  Act {result.actReached}{' '}
                  <span className="rr__stat-sub">({result.nodesVisited})</span>
                </dd>
              </div>
              <div className="rr__stat-line">
                <dt className="rr__stat-label">プレイ時間</dt>
                <dd className="rr__stat-value">{formatSeconds(result.playSeconds)}</dd>
              </div>
              <div className="rr__stat-line">
                <dt className="rr__stat-label">HP</dt>
                <dd className="rr__stat-value">
                  {result.finalHp} / {result.finalMaxHp}
                </dd>
              </div>
              <div className="rr__stat-line">
                <dt className="rr__stat-label">GOLD</dt>
                <dd className="rr__stat-value rr__stat-value--gold">{result.finalGold}</dd>
              </div>
            </dl>

            <div className="rr__trail-block">
              <div className="rr__trail-main">
                <div className="rr__trail-header">
                  <span>走行履歴</span>
                </div>
                {/*
                  Per-node trail data is not yet wired through RunResultDto;
                  render compact act-summary placeholder rows so the layout
                  from the canonical mockup is preserved visually.
                */}
                <div className="rr__journey-row">
                  <div className="rr__journey-act-label">
                    ACT {result.actReached >= 1 ? 'I' : '-'}
                  </div>
                  <div className="rr__journey-nodes" aria-hidden="true">
                    {Array.from({ length: 17 }).map((_, i) => (
                      <div
                        key={i}
                        className={`rr__journey-node ${i === 0 ? 'node--start' : 'node--empty'}`}
                      >
                        {i === 0 ? 'S' : ''}
                      </div>
                    ))}
                  </div>
                </div>
              </div>

              <div className="rr__trail-legend" aria-hidden="true">
                <span className="lg--start">開始</span>
                <span className="lg--boss">ボス</span>
                <span className="lg--fight">戦闘</span>
                <span className="lg--elite">精鋭</span>
                <span className="lg--event">イベント</span>
                <span className="lg--merchant">商店</span>
                <span className="lg--rest">休憩</span>
                <span className="lg--treasure">宝箱</span>
              </div>
            </div>
          </div>
        </section>

        {/* ---- Inventory grid (Relics / Potions / Deck) ---- */}
        <div className="rr__grid">

          <section className="rr__panel" aria-label="Relics">
            <div className="rr__panel-title-row">
              <div className="rr__panel-title">▸ RELICS · {relicCount}</div>
            </div>
            <div className="rr__scroll">
              {relicCount === 0 ? (
                <div className="rr__empty">なし</div>
              ) : (
                <ul className="rr__items">
                  {result.finalRelics.map((r) => (
                    <li key={r} className="rr__item item--common">
                      <div className="rr__item-icon" aria-hidden="true">ICON</div>
                      <div className="rr__item-name">{r}</div>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </section>

          <section className="rr__panel" aria-label="Potions">
            <div className="rr__panel-title-row">
              <div className="rr__panel-title">
                ▸ POTIONS · {result.acquiredPotionIds.length}
              </div>
            </div>
            <div className="rr__scroll">
              {result.acquiredPotionIds.length === 0 ? (
                <div className="rr__empty">なし</div>
              ) : (
                <ul className="rr__items">
                  {result.acquiredPotionIds.map((p, i) => (
                    <li key={`${p}-${i}`} className="rr__item item--common">
                      <div className="rr__item-icon" aria-hidden="true">ICON</div>
                      <div className="rr__item-name">{p}</div>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </section>

          <section className="rr__panel" aria-label="Deck">
            <div className="rr__panel-title-row">
              <div className="rr__panel-title">▸ DECK · {deckCount}</div>
            </div>
            <div className="rr__scroll">
              {deckCount === 0 ? (
                <div className="rr__empty">なし</div>
              ) : (
                <ul className="rr__items rr__items--2col">
                  {result.finalDeck.map((c, i) => (
                    <li key={`${c.id}-${i}`} className="rr__item item--common">
                      <div className="rr__item-icon" aria-hidden="true">CARD</div>
                      <div className="rr__item-name">
                        {c.id}
                        {c.upgraded ? <span className="rr__item-plus">+</span> : null}
                      </div>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </section>
        </div>

        {/* ---- Footer ---- */}
        <div className="rr__footer">
          <button
            type="button"
            className="rr__btn"
            onClick={onReturnToMenu}
            aria-label="メニューへ戻る"
          >
            メニューへ戻る
          </button>
        </div>
      </div>
    </main>
  )
}
