import { useEffect, useMemo, useState } from 'react'
import { getBestiary } from '../api/bestiary'
import { getHistory } from '../api/history'
import type { BestiaryDto, RunResultDto } from '../api/types'
import { Card } from '../components/Card'
import type { CardRarity } from '../components/Card'
import { cardDisplay } from '../components/cardDisplay'
import { useTooltipTarget } from '../components/Tooltip'
import type { TooltipContent } from '../components/Tooltip'
import { useRelicCatalog } from '../hooks/useRelicCatalog'
import { useCardCatalog, usePotionCatalog } from '../hooks/useCardCatalog'
import './AchievementsScreen.css'

function relicRarityCode(rarity: string | undefined): CardRarity | undefined {
  if (!rarity) return undefined
  const r = rarity.toLowerCase()
  if (r === 'rare' || r === 'r') return 'r'
  if (r === 'epic' || r === 'e') return 'e'
  if (r === 'legendary' || r === 'l') return 'l'
  return 'c'
}

function potionRarityCode(n: number | undefined): CardRarity | undefined {
  if (n === undefined) return undefined
  switch (n) {
    case 0: return 'c'
    case 1: return 'r'
    case 2: return 'e'
    case 3: return 'l'
    default: return 'c'
  }
}

const RARITY_ORDER: CardRarity[] = ['c', 'r', 'e', 'l']
const RARITY_LABEL: Record<CardRarity, string> = {
  c: 'COMMON',
  r: 'RARE',
  e: 'EPIC',
  l: 'LEGENDARY',
}

/** Card-type filter values. 'summon' is reserved for a future card type. */
type CardTypeFilter = 'all' | 'attack' | 'skill' | 'power' | 'summon'

const CARD_TYPE_FILTERS: { value: CardTypeFilter; label: string }[] = [
  { value: 'all', label: '全て' },
  { value: 'attack', label: 'アタック' },
  { value: 'skill', label: 'スキル' },
  { value: 'power', label: 'パワー' },
  { value: 'summon', label: 'サモン' },
]

function groupByRarity<T>(items: T[], rarityOf: (item: T) => CardRarity | undefined): Map<CardRarity | 'unknown', T[]> {
  const groups = new Map<CardRarity | 'unknown', T[]>()
  for (const r of RARITY_ORDER) groups.set(r, [])
  groups.set('unknown', [])
  for (const item of items) {
    const r = rarityOf(item)
    const key = r ?? 'unknown'
    groups.get(key)!.push(item)
  }
  return groups
}

function BackButton({ onBack }: { onBack: () => void }) {
  return (
    <button type="button" className="achievements__back" onClick={onBack}>
      <span className="achievements__back-mark" aria-hidden="true">◂</span>
      メニューへ戻る
    </button>
  )
}

type Tab = 'cards' | 'relics' | 'potions' | 'enemies' | 'history'

type Props = {
  accountId: string
  onBack: () => void
}

export function AchievementsScreen({ accountId, onBack }: Props) {
  const [tab, setTab] = useState<Tab>('cards')
  const [bestiary, setBestiary] = useState<BestiaryDto | null>(null)
  const [history, setHistory] = useState<RunResultDto[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    Promise.all([getBestiary(accountId), getHistory(accountId)])
      .then(([b, h]) => {
        if (cancelled) return
        setBestiary(b)
        setHistory(h)
      })
      .catch(() => { if (!cancelled) setError('読み込みに失敗しました') })
    return () => { cancelled = true }
  }, [accountId])

  if (error) return (
    <main className="achievements">
      <div className="achievements__pattern" aria-hidden="true" />
      <div className="achievements__placeholder">
        <p>{error}</p>
        <BackButton onBack={onBack} />
      </div>
    </main>
  )
  if (bestiary === null || history === null) return (
    <main className="achievements">
      <div className="achievements__pattern" aria-hidden="true" />
      <div className="achievements__placeholder"><p>読み込み中...</p></div>
    </main>
  )

  const counts = {
    cards: { found: bestiary.discoveredCardBaseIds.length, total: bestiary.allKnownCardBaseIds.length },
    relics: { found: bestiary.discoveredRelicIds.length, total: bestiary.allKnownRelicIds.length },
    potions: { found: bestiary.discoveredPotionIds.length, total: bestiary.allKnownPotionIds.length },
    enemies: { found: bestiary.encounteredEnemyIds.length, total: bestiary.allKnownEnemyIds.length },
    history: history.length,
  }

  return (
    <main className="achievements">
      <div className="achievements__pattern" aria-hidden="true" />
      <div className="achievements__inner">
        <header className="achievements__header">
          <h1 className="achievements__title">❖ ARCHIVES ❖</h1>
          <div className="achievements__ornament" aria-hidden="true">✦ ✦ ✦</div>
        </header>

        <div className="achievements__tabs" role="tablist">
          <TabButton label="カード" count={`${counts.cards.found} / ${counts.cards.total}`}
            active={tab === 'cards'} onClick={() => setTab('cards')} />
          <TabButton label="レリック" count={`${counts.relics.found} / ${counts.relics.total}`}
            active={tab === 'relics'} onClick={() => setTab('relics')} />
          <TabButton label="ポーション" count={`${counts.potions.found} / ${counts.potions.total}`}
            active={tab === 'potions'} onClick={() => setTab('potions')} />
          <TabButton label="モンスター" count={`${counts.enemies.found} / ${counts.enemies.total}`}
            active={tab === 'enemies'} onClick={() => setTab('enemies')} />
          <TabButton label="履歴" count={`${counts.history}`}
            active={tab === 'history'} onClick={() => setTab('history')} />
        </div>

        <section className="achievements__content">
          {tab === 'cards' && (
            <CardsTab
              allIds={bestiary.allKnownCardBaseIds}
              discovered={new Set(bestiary.discoveredCardBaseIds)} />
          )}
          {tab === 'relics' && (
            <TilesTab
              title="レリック"
              kind="relic"
              allIds={bestiary.allKnownRelicIds}
              discovered={new Set(bestiary.discoveredRelicIds)} />
          )}
          {tab === 'potions' && (
            <TilesTab
              title="ポーション"
              kind="potion"
              allIds={bestiary.allKnownPotionIds}
              discovered={new Set(bestiary.discoveredPotionIds)} />
          )}
          {tab === 'enemies' && (
            <TilesTab
              title="モンスター"
              kind="enemy"
              allIds={bestiary.allKnownEnemyIds}
              discovered={new Set(bestiary.encounteredEnemyIds)} />
          )}
          {tab === 'history' && <HistoryList history={history} />}
        </section>

        <footer className="achievements__footer">
          <BackButton onBack={onBack} />
        </footer>
      </div>
    </main>
  )
}

function TabButton({ label, count, active, onClick }: {
  label: string; count?: string; active: boolean; onClick: () => void
}) {
  return (
    <button
      role="tab"
      aria-selected={active}
      onClick={onClick}
      className={'achievements__tab' + (active ? ' achievements__tab--active' : '')}
    >
      <span>{label}</span>
      {count !== undefined && <span className="achievements__tab-count">{count}</span>}
    </button>
  )
}

/* ------------------------------------------------------------------
   Cards tab — uses the shared <Card> primitive for each tile.
   The primitive is itself the canonical v12 card; we pass locked/unknown
   variant for undiscovered entries.
   ------------------------------------------------------------------ */
function CardsTab({ allIds, discovered }: { allIds: string[]; discovered: Set<string> }) {
  const { catalog: cardCatalog } = useCardCatalog()
  const [typeFilter, setTypeFilter] = useState<CardTypeFilter>('all')

  // Resolve display info up-front so we can filter and group by rarity/type
  // without recomputing per render branch. Undiscovered cards still get a
  // catalog lookup so we can group them by rarity even before reveal.
  const items = useMemo(() => {
    return allIds.map(id => {
      const disp = cardDisplay(id, cardCatalog)
      return { id, disp, isDiscovered: discovered.has(id) }
    })
  }, [allIds, cardCatalog, discovered])

  const filtered = useMemo(() => {
    if (typeFilter === 'all') return items
    return items.filter(it => it.disp.type === typeFilter)
  }, [items, typeFilter])

  const groups = useMemo(
    () => groupByRarity(filtered, it => it.disp.rarity),
    [filtered],
  )

  return (
    <div className="achievements__panel">
      <div className="achievements__panel-title-row">
        <div className="achievements__panel-title">▸ CARDS</div>
        <p className="achievements__count">{discovered.size} / {allIds.length} 発見</p>
      </div>
      <div className="achievements__filter-row" role="tablist" aria-label="カードタイプで絞り込み">
        {CARD_TYPE_FILTERS.map(f => (
          <button
            key={f.value}
            type="button"
            role="tab"
            aria-selected={typeFilter === f.value ? 'true' : 'false'}
            className={
              'achievements__filter-chip'
              + (typeFilter === f.value ? ' achievements__filter-chip--active' : '')
            }
            onClick={() => setTypeFilter(f.value)}
          >
            {f.label}
          </button>
        ))}
      </div>
      <div className="achievements__scroll">
        {RARITY_ORDER.map(rarity => {
          const list = groups.get(rarity) ?? []
          if (list.length === 0) return null
          return (
            <section key={rarity} className="achievements__rarity-group">
              <h3 className={`achievements__rarity-heading achievements__rarity-heading--${rarity}`}>
                {RARITY_LABEL[rarity]} <span className="achievements__rarity-count">({list.length})</span>
              </h3>
              <div className="achievements__card-grid">
                {list.map(({ id, disp, isDiscovered }) => (
                  <div key={id} className="achievements__card-cell">
                    {isDiscovered ? (
                      <Card
                        name={disp.name}
                        cost={disp.cost}
                        type={disp.type}
                        rarity={disp.rarity}
                        description={disp.description}
                        upgradedDescription={disp.upgradedDescription}
                      />
                    ) : (
                      <Card name="? ? ?" cost={0} type="skill" rarity={disp.rarity} locked />
                    )}
                    <span className="achievements__card-id">
                      {isDiscovered ? `${disp.name} (${id})` : `??? (${id})`}
                    </span>
                  </div>
                ))}
              </div>
            </section>
          )
        })}
      </div>
    </div>
  )
}

/* ------------------------------------------------------------------
   Tiles tab — relics / potions / enemies.
   Discovered relic/potion tiles render real PNG icons and a hover
   tooltip showing the entry's name + description; enemies still use
   the ☠ glyph since enemy art is pending.
   ------------------------------------------------------------------ */
type TileKind = 'relic' | 'potion' | 'enemy'

function TilesTab({ title, kind, allIds, discovered }: {
  title: string; kind: TileKind; allIds: string[]; discovered: Set<string>
}) {
  const { catalog: relicCatalog, names: relicNames } = useRelicCatalog()
  const { catalog: potionCatalog, names: potionNames } = usePotionCatalog()

  const items = useMemo(() => allIds.map(id => {
    const isDiscovered = discovered.has(id)
    const displayName =
      kind === 'relic' ? (relicNames[id] ?? id)
      : kind === 'potion' ? (potionNames[id] ?? id)
      : id
    const description =
      kind === 'relic' ? (relicCatalog?.[id]?.description ?? null)
      : kind === 'potion' ? (potionCatalog?.[id]?.description ?? null)
      : null
    const rarity =
      kind === 'relic' ? relicRarityCode(relicCatalog?.[id]?.rarity)
      : kind === 'potion' ? potionRarityCode(potionCatalog?.[id]?.rarity)
      : undefined
    return { id, isDiscovered, displayName, description, rarity }
  }), [allIds, discovered, kind, relicCatalog, relicNames, potionCatalog, potionNames])

  // Enemies have no rarity — render flat. Relics/potions group by rarity.
  const flat = kind === 'enemy'

  return (
    <div className="achievements__panel">
      <div className="achievements__panel-title-row">
        <div className="achievements__panel-title">▸ {title.toUpperCase()}</div>
        <p className="achievements__count">{discovered.size} / {allIds.length} 発見</p>
      </div>
      <div className="achievements__scroll">
        {flat ? (
          <div className="achievements__tile-grid">
            {items.map(it => (
              <BestiaryTile
                key={it.id}
                kind={kind}
                id={it.id}
                isDiscovered={it.isDiscovered}
                displayName={it.displayName}
                description={it.description}
                rarity={it.rarity}
              />
            ))}
          </div>
        ) : (
          RARITY_ORDER.map(rarity => {
            const list = items.filter(it => it.rarity === rarity)
            if (list.length === 0) return null
            return (
              <section key={rarity} className="achievements__rarity-group">
                <h3 className={`achievements__rarity-heading achievements__rarity-heading--${rarity}`}>
                  {RARITY_LABEL[rarity]} <span className="achievements__rarity-count">({list.length})</span>
                </h3>
                <div className="achievements__tile-grid">
                  {list.map(it => (
                    <BestiaryTile
                      key={it.id}
                      kind={kind}
                      id={it.id}
                      isDiscovered={it.isDiscovered}
                      displayName={it.displayName}
                      description={it.description}
                      rarity={it.rarity}
                    />
                  ))}
                </div>
              </section>
            )
          })
        )}
      </div>
    </div>
  )
}

function BestiaryTile({ kind, id, isDiscovered, displayName, description, rarity }: {
  kind: TileKind
  id: string
  isDiscovered: boolean
  displayName: string
  description: string | null
  rarity?: CardRarity
}) {
  const tooltipContent = useMemo<TooltipContent | null>(() => {
    if (!isDiscovered) return null
    return { name: displayName, rarity, desc: description ?? '—' }
  }, [isDiscovered, displayName, description, rarity])
  const tip = useTooltipTarget(tooltipContent)

  const art = (() => {
    if (!isDiscovered) return <span aria-hidden="true">?</span>
    if (kind === 'relic') return <img src={`/icons/relics/${id}.png`} alt="" draggable={false} />
    if (kind === 'potion') return <img src={`/icons/potions/${id}.png`} alt="" draggable={false} />
    return <span aria-hidden="true">☠</span>
  })()

  const tileClasses = [
    'achievements__tile',
    isDiscovered && rarity ? `achievements__tile--rarity-${rarity}` : null,
  ].filter(Boolean).join(' ')

  return (
    <div
      className={'achievements__tile-cell' + (isDiscovered ? '' : ' is-locked')}
      onMouseEnter={tip.onMouseEnter}
      onMouseMove={tip.onMouseMove}
      onMouseLeave={tip.onMouseLeave}
    >
      <div className={tileClasses}>
        <div className="achievements__tile-bg" aria-hidden="true" />
        <div className="achievements__tile-frame" aria-hidden="true" />
        <div className="achievements__tile-art" aria-hidden="true">
          {art}
        </div>
      </div>
      <div className="achievements__tile-name">
        {isDiscovered ? (
          <span>{displayName}</span>
        ) : (
          <span>??? ({id})</span>
        )}
      </div>
    </div>
  )
}

/* ------------------------------------------------------------------
   History tab — run rows with expand-on-click detail.
   ------------------------------------------------------------------ */
function HistoryList({ history }: { history: RunResultDto[] }) {
  const [expanded, setExpanded] = useState<string | null>(null)
  if (history.length === 0) {
    return (
      <div className="achievements__panel">
        <div className="achievements__panel-title-row">
          <div className="achievements__panel-title">▸ RUN HISTORY</div>
          <p className="achievements__count">0 件</p>
        </div>
        <p className="achievements__history-empty">履歴なし</p>
      </div>
    )
  }
  return (
    <div className="achievements__panel">
      <div className="achievements__panel-title-row">
        <div className="achievements__panel-title">▸ RUN HISTORY</div>
        <p className="achievements__count">{history.length} 件</p>
      </div>
      <div className="achievements__scroll">
        <ul className="achievements__history">
          {history.map(run => {
            const isOpen = expanded === run.runId
            return (
              <li key={run.runId} className="achievements__history-entry">
                <button
                  className={
                    'achievements__history-summary' + (isOpen ? ' is-open' : '')
                  }
                  onClick={() => setExpanded(isOpen ? null : run.runId)}
                >
                  {`[${run.outcome}] Act${run.actReached} / ${formatPlayTime(run.playSeconds)} / ${run.endedAtUtc}`}
                </button>
                {isOpen && <HistoryDetail run={run} />}
              </li>
            )
          })}
        </ul>
      </div>
    </div>
  )
}

function HistoryDetail({ run }: { run: RunResultDto }) {
  return (
    <div className="achievements__history-detail">
      <p>最終 HP: {run.finalHp}/{run.finalMaxHp}</p>
      <p>最終ゴールド: {run.finalGold}</p>
      <p>最終デッキ: {run.finalDeck.length === 0
        ? '（なし）'
        : run.finalDeck.map(c => c.id + (c.upgraded ? '+' : '')).join(', ')}</p>
      <p>最終レリック数: {run.finalRelics.length}</p>
      <p>見たカード: {run.seenCardBaseIds.length === 0 ? '（なし）' : run.seenCardBaseIds.join(', ')}</p>
      <p>入手レリック: {run.acquiredRelicIds.length === 0 ? '（なし）' : run.acquiredRelicIds.join(', ')}</p>
      <p>入手ポーション: {run.acquiredPotionIds.length === 0 ? '（なし）' : run.acquiredPotionIds.join(', ')}</p>
      <p>遭遇敵: {run.encounteredEnemyIds.length === 0 ? '（なし）' : run.encounteredEnemyIds.join(', ')}</p>
    </div>
  )
}

function formatPlayTime(seconds: number): string {
  const m = Math.floor(seconds / 60)
  const s = Math.floor(seconds % 60)
  return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`
}
