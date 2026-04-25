import type { ReactNode } from 'react'
import type { RunResultDto, RunSnapshotDto, TileKind } from '../api/types'
import './RunResultScreen.css'

const TILE_IMG_SRC: Record<Exclude<TileKind, 'Start'>, string> = {
  Enemy: '/icons/tiles/enemy.png',
  Elite: '/icons/tiles/elite.png',
  Merchant: '/icons/tiles/merchant.png',
  Rest: '/icons/tiles/rest.png',
  Treasure: '/icons/tiles/treasure.png',
  Event: '/icons/tiles/event.png',
  Unknown: '/icons/tiles/unknown.png',
  Boss: '/icons/tiles/boss.png',
}

type Props = {
  result: RunResultDto
  snapshot?: RunSnapshotDto
  onReturnToMenu: () => void
}

function journeyIcon(kind: TileKind, resolvedKind: TileKind | null): ReactNode {
  const k = kind === 'Unknown' && resolvedKind === null ? 'Unknown' : (resolvedKind ?? kind)
  if (k === 'Start') return '●'
  return <img src={TILE_IMG_SRC[k]} alt="" className="rr__journey-img" draggable={false} />
}

function journeyNodeClass(kind: TileKind, resolvedKind: TileKind | null): string {
  const k = kind === 'Unknown' && resolvedKind === null ? 'Unknown' : (resolvedKind ?? kind)
  switch (k) {
    case 'Start': return 'node--start'
    case 'Enemy': return 'node--fight'
    case 'Elite': return 'node--elite'
    case 'Event': return 'node--event'
    case 'Merchant': return 'node--merchant'
    case 'Rest': return 'node--rest'
    case 'Treasure': return 'node--treasure'
    case 'Boss': return 'node--boss'
    case 'Unknown': return 'node--empty'
  }
}

function journeyTooltip(kind: TileKind, resolvedKind: TileKind | null): string {
  const k = kind === 'Unknown' && resolvedKind === null ? 'Unknown' : (resolvedKind ?? kind)
  switch (k) {
    case 'Start': return '開始地点'
    case 'Enemy': return '敵との戦闘'
    case 'Elite': return 'エリート戦'
    case 'Merchant': return '商人'
    case 'Rest': return '休憩'
    case 'Treasure': return '宝箱'
    case 'Event': return 'イベント'
    case 'Unknown': return '未知'
    case 'Boss': return 'ボス戦'
  }
}

function actLabel(act: number): string {
  const romans = ['I', 'II', 'III', 'IV', 'V', 'VI']
  return romans[act - 1] ?? String(act)
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

export function RunResultScreen({ result, snapshot, onReturnToMenu }: Props) {
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
                {snapshot ? (
                  <div className="rr__journey-row">
                    <div className="rr__journey-act-label">
                      ACT {actLabel(snapshot.run.currentAct)}
                    </div>
                    <div className="rr__journey-nodes">
                      {snapshot.run.visitedNodeIds.map((nid, i) => {
                        const node = snapshot.map.nodes.find(x => x.id === nid)
                        if (!node) return null
                        const resolved = snapshot.run.unknownResolutions[nid] ?? null
                        const icon = journeyIcon(node.kind, resolved)
                        const cls = journeyNodeClass(node.kind, resolved)
                        const tip = journeyTooltip(node.kind, resolved)
                        return (
                          <div
                            key={`${nid}-${i}`}
                            className={`rr__journey-node ${cls}`}
                            title={tip}
                            aria-label={tip}
                          >
                            <span aria-hidden="true">{icon}</span>
                          </div>
                        )
                      })}
                    </div>
                  </div>
                ) : (
                  <div className="rr__journey-row">
                    <div className="rr__journey-act-label">
                      ACT {result.actReached >= 1 ? actLabel(result.actReached) : '-'}
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
                )}
              </div>

              <div className="rr__trail-legend" aria-hidden="true">
                <span className="lg--start"><span className="lg__sym">●</span>開始</span>
                <span className="lg--boss"><span className="lg__sym"><img src="/icons/tiles/boss.png" alt="" /></span>ボス</span>
                <span className="lg--fight"><span className="lg__sym"><img src="/icons/tiles/enemy.png" alt="" /></span>戦闘</span>
                <span className="lg--elite"><span className="lg__sym"><img src="/icons/tiles/elite.png" alt="" /></span>精鋭</span>
                <span className="lg--event"><span className="lg__sym"><img src="/icons/tiles/event.png" alt="" /></span>イベント</span>
                <span className="lg--merchant"><span className="lg__sym"><img src="/icons/tiles/merchant.png" alt="" /></span>商店</span>
                <span className="lg--rest"><span className="lg__sym"><img src="/icons/tiles/rest.png" alt="" /></span>休憩</span>
                <span className="lg--treasure"><span className="lg__sym"><img src="/icons/tiles/treasure.png" alt="" /></span>宝箱</span>
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
                      <div className="rr__item-icon" aria-hidden="true">
                        <img src={`/icons/relics/${r}.png`} alt="" draggable={false} />
                      </div>
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
                      <div className="rr__item-icon" aria-hidden="true">
                        <img src={`/icons/potions/${p}.png`} alt="" draggable={false} />
                      </div>
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
