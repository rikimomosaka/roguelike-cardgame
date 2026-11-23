import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { getBestiary } from '../api/bestiary'
import { getHistory } from '../api/history'
import type { BestiaryCharacterDto, BestiaryDto, RunResultDto } from '../api/types'
import { Card, type CardRarity, type CardType } from '../components/Card'
import { ActorStats, formatActorStats } from '../components/ActorStats'
import { CardDesc, type CardDescTerm } from '../components/CardDesc'
import { cardDisplay } from '../components/cardDisplay'
import type { CardPoolClass, CharacterClass } from '../api/catalog'
import { optionalRarityCodeFromNumber, rarityCodeFromName, rarityCodeFromNumber } from '../components/rarity'
import { useTooltipTarget } from '../components/Tooltip'
import type { TooltipContent } from '../components/Tooltip'
import { getRelicTooltipContent } from '../components/relicTooltip'
import { useRelicCatalog } from '../hooks/useRelicCatalog'
import { useCardCatalog, usePotionCatalog } from '../hooks/useCardCatalog'
import { useEnemyCatalog } from '../hooks/useEnemyCatalog'
import {
  DEFAULT_CHARACTER_PORTRAIT,
  resolveCharacterPortrait,
} from '../presentation/playableCharacterVisuals'
import './AchievementsScreen.css'

// Why: Token rarity (Phase 10.5.G) は achievements (図鑑) の通常コレクション
//   対象外なので RARITY_ORDER には含めない。RARITY_LABEL は CardRarity 全
//   バリアントを網羅する Record 型なので 't' エントリを置いておく
//   (ラベル名は他レア同様に大文字スネーク)。
const RARITY_ORDER: CardRarity[] = ['c', 'r', 'e', 'l']
const RARITY_LABEL: Record<CardRarity, string> = {
  c: 'COMMON',
  r: 'RARE',
  e: 'EPIC',
  l: 'LEGENDARY',
  t: 'TOKEN',
}

type CardTypeFilter = 'all' | 'attack' | 'skill' | 'power' | 'unit'
type CardPoolFilter = 'all' | CardPoolClass | 'unpooled'
type CharacterClassFilter = 'all' | CharacterClass
type DiscoveryFilter = 'all' | 'discovered' | 'undiscovered'

const CARD_TYPE_FILTERS: { value: CardTypeFilter; label: string }[] = [
  { value: 'all', label: '全て' },
  { value: 'attack', label: 'アタック' },
  { value: 'skill', label: 'スキル' },
  { value: 'power', label: 'パワー' },
  { value: 'unit', label: 'サモン' },
]

const CARD_POOL_FILTERS: { value: CardPoolFilter; label: string }[] = [
  { value: 'all', label: 'すべて' },
  { value: 'Striker', label: 'ストライカー' },
  { value: 'Guardian', label: 'ガーディアン' },
  { value: 'Tactician', label: 'タクティシャン' },
  { value: 'Enhancer', label: 'エンハンサー' },
  { value: 'Universal', label: 'ユニバーサル' },
  { value: 'unpooled', label: 'プール外' },
]

const CHARACTER_CLASS_FILTERS: { value: CharacterClassFilter; label: string }[] = [
  { value: 'all', label: 'すべて' },
  { value: 'Striker', label: 'ストライカー' },
  { value: 'Guardian', label: 'ガーディアン' },
  { value: 'Tactician', label: 'タクティシャン' },
  { value: 'Enhancer', label: 'エンハンサー' },
]

function groupByRarity<T>(
  items: T[],
  rarityOf: (item: T) => CardRarity | undefined,
): Map<CardRarity | 'unknown', T[]> {
  const groups = new Map<CardRarity | 'unknown', T[]>()
  for (const rarity of RARITY_ORDER) groups.set(rarity, [])
  groups.set('unknown', [])
  for (const item of items) {
    groups.get(rarityOf(item) ?? 'unknown')!.push(item)
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

type Tab = 'characters' | 'cards' | 'relics' | 'potions' | 'enemies' | 'history'

type Props = {
  accountId: string
  onBack: () => void
}

export function AchievementsScreen({ accountId, onBack }: Props) {
  const [tab, setTab] = useState<Tab>('characters')
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
    characters: {
      found: [...bestiary.playerCharacters, ...bestiary.allyCharacters]
        .filter(character => character.isDiscovered).length,
      total: bestiary.playerCharacters.length + bestiary.allyCharacters.length,
    },
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
          <TabButton label="キャラクター" count={`${counts.characters.found} / ${counts.characters.total}`}
            active={tab === 'characters'} onClick={() => setTab('characters')} />
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
          {tab === 'characters' && (
            <CharactersTab
              players={bestiary.playerCharacters}
              allies={bestiary.allyCharacters} />
          )}
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

function CardsTab({ allIds, discovered }: { allIds: string[]; discovered: Set<string> }) {
  const { catalog: cardCatalog } = useCardCatalog()
  const [typeFilter, setTypeFilter] = useState<CardTypeFilter>('all')
  const [poolFilter, setPoolFilter] = useState<CardPoolFilter>('all')
  const [query, setQuery] = useState('')
  const [discoveryFilter, setDiscoveryFilter] = useState<DiscoveryFilter>('all')

  const items = useMemo(() => allIds.map(id => ({
    id,
    disp: cardDisplay(id, cardCatalog),
    poolClass: cardCatalog === null ? undefined : (cardCatalog[id]?.poolClass ?? null),
    isDiscovered: discovered.has(id),
  })), [allIds, cardCatalog, discovered])

  const filtered = useMemo(() => items.filter(item => {
    const matchesType = typeFilter === 'all' || item.disp.type === typeFilter
    const matchesPool = poolFilter === 'all'
      || (poolFilter === 'unpooled'
        ? item.poolClass === null
        : item.poolClass === poolFilter)
    const matchesDiscovery = discoveryFilter === 'all'
      || (discoveryFilter === 'discovered' ? item.isDiscovered : !item.isDiscovered)
    const normalizedQuery = query.trim().toLocaleLowerCase('ja')
    // Locked entries never expose their internal id through search.
    const matchesQuery = normalizedQuery.length === 0
      || (item.isDiscovered && item.disp.name.toLocaleLowerCase('ja').includes(normalizedQuery))
    return matchesType && matchesPool && matchesDiscovery && matchesQuery
  }), [discoveryFilter, items, poolFilter, query, typeFilter])
  const discoveredCount = filtered.filter(item => item.isDiscovered).length
  const groups = useMemo(
    () => groupByRarity(filtered, item => item.disp.rarity),
    [filtered],
  )

  return (
    <div className="achievements__panel">
      <div className="achievements__panel-title-row">
        <div className="achievements__panel-title">▸ CARDS</div>
        <p className="achievements__count">{discoveredCount} / {filtered.length} 発見</p>
      </div>
      <ArchiveTools
        label="カード"
        query={query}
        onQuery={setQuery}
        discovery={discoveryFilter}
        onDiscovery={setDiscoveryFilter}
      />
      <div className="achievements__filter-row" role="group" aria-label="カードタイプで絞り込み">
        <span className="achievements__filter-label">タイプ</span>
        {CARD_TYPE_FILTERS.map(filter => (
          <button
            key={filter.value}
            type="button"
            aria-pressed={typeFilter === filter.value}
            className={'achievements__filter-chip'
              + (typeFilter === filter.value ? ' achievements__filter-chip--active' : '')}
            onClick={() => setTypeFilter(filter.value)}
          >
            {filter.label}
          </button>
        ))}
      </div>
      <div className="achievements__filter-row" role="group" aria-label="カード所属で絞り込み">
        <span className="achievements__filter-label">所属</span>
        {CARD_POOL_FILTERS.map(filter => (
          <button
            key={filter.value}
            type="button"
            aria-pressed={poolFilter === filter.value}
            className={'achievements__filter-chip'
              + (poolFilter === filter.value ? ' achievements__filter-chip--active' : '')}
            onClick={() => setPoolFilter(filter.value)}
          >
            {filter.label}
          </button>
        ))}
      </div>
      <div className="achievements__scroll">
        {filtered.length === 0 && <p className="achievements__empty">該当するカードはありません。</p>}
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
                  <div
                    key={id}
                    className={'achievements__card-cell'
                      + (isDiscovered ? '' : ' is-undiscovered')}
                    role={isDiscovered ? undefined : 'img'}
                    aria-label={isDiscovered ? undefined : '未発見カード'}
                  >
                    <Card
                      name={disp.name}
                      cost={disp.cost}
                      type={disp.type}
                      rarity={disp.rarity}
                      poolClass={disp.poolClass}
                      description={disp.description}
                      upgradedDescription={disp.upgradedDescription}
                      detailsDisabled={!isDiscovered}
                      ariaHidden={!isDiscovered}
                    />
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

type CharacterFilter = 'player' | 'ally'

function CharactersTab({ players, allies }: {
  players: BestiaryCharacterDto[]
  allies: BestiaryCharacterDto[]
}) {
  const [filter, setFilter] = useState<CharacterFilter>('player')
  const [classFilter, setClassFilter] = useState<CharacterClassFilter>('all')
  const [query, setQuery] = useState('')
  const [discoveryFilter, setDiscoveryFilter] = useState<DiscoveryFilter>('all')
  const sourceCharacters = filter === 'player' ? players : allies
  const classFiltered = filter === 'player' && classFilter !== 'all'
    ? sourceCharacters.filter(character => character.characterClass === classFilter)
    : sourceCharacters
  const normalizedQuery = query.trim().toLocaleLowerCase('ja')
  const characters = classFiltered.filter(character => {
    const matchesDiscovery = discoveryFilter === 'all'
      || (discoveryFilter === 'discovered' ? character.isDiscovered : !character.isDiscovered)
    const matchesQuery = normalizedQuery.length === 0
      || (character.isDiscovered && character.name.toLocaleLowerCase('ja').includes(normalizedQuery))
    return matchesDiscovery && matchesQuery
  })
  const discoveredCount = characters.filter(character => character.isDiscovered).length

  return (
    <div className="achievements__panel">
      <div className="achievements__panel-title-row">
        <div className="achievements__panel-title">▸ CHARACTERS</div>
        <p className="achievements__count">{discoveredCount} / {characters.length} 発見</p>
      </div>
      <ArchiveTools
        label="キャラクター"
        query={query}
        onQuery={setQuery}
        discovery={discoveryFilter}
        onDiscovery={setDiscoveryFilter}
      />
      <div className="achievements__filter-row" role="tablist" aria-label="キャラクター種別">
        <button
          type="button"
          role="tab"
          aria-selected={filter === 'player'}
          className={'achievements__filter-chip'
            + (filter === 'player' ? ' achievements__filter-chip--active' : '')}
          onClick={() => setFilter('player')}
        >
          プレイヤー
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={filter === 'ally'}
          className={'achievements__filter-chip'
            + (filter === 'ally' ? ' achievements__filter-chip--active' : '')}
          onClick={() => setFilter('ally')}
        >
          味方
        </button>
      </div>
      {filter === 'player' && (
        <div className="achievements__filter-row" role="group" aria-label="キャラクタークラスで絞り込み">
          <span className="achievements__filter-label">クラス</span>
          {CHARACTER_CLASS_FILTERS.map(option => (
            <button
              key={option.value}
              type="button"
              aria-pressed={classFilter === option.value}
              className={'achievements__filter-chip'
                + (classFilter === option.value ? ' achievements__filter-chip--active' : '')}
              onClick={() => setClassFilter(option.value)}
            >
              {option.label}
            </button>
          ))}
        </div>
      )}
      <div className="achievements__scroll">
        {characters.length === 0 && <p className="achievements__empty">該当するキャラクターはいません。</p>}
        <div className="achievements__character-grid">
          {characters.map(character => (
            <CharacterTile key={character.id} character={character} />
          ))}
        </div>
      </div>
    </div>
  )
}

function CharacterTile({ character }: { character: BestiaryCharacterDto }) {
  const characterClass = character.characterClass ?? null
  const image = character.kind === 'player'
    ? resolveCharacterPortrait(character.id)
    : character.imageId
      ? `/characters/units/${character.imageId}.png`
      : null

  return (
    <div
      className={'achievements__character-cell'
        + (character.isDiscovered ? '' : ' is-locked')
        + (characterClass ? ` achievements__character-cell--${characterClass.toLowerCase()}` : '')}
    >
      <div className="achievements__character-summary">
        <div className="achievements__character-portrait">
          {character.isDiscovered ? (
            image
              ? (
                <img
                  src={image}
                  alt=""
                  draggable={false}
                  onError={event => {
                    if (character.kind === 'player'
                      && event.currentTarget.getAttribute('src') !== DEFAULT_CHARACTER_PORTRAIT) {
                      event.currentTarget.src = DEFAULT_CHARACTER_PORTRAIT
                      return
                    }
                    event.currentTarget.hidden = true
                    event.currentTarget.nextElementSibling?.removeAttribute('hidden')
                  }}
                />
              )
              : <span className="achievements__character-glyph" aria-hidden="true">✦</span>
          ) : (
            <span className="achievements__character-glyph" aria-hidden="true">?</span>
          )}
          {character.isDiscovered && image ? (
            <span className="achievements__character-glyph" aria-hidden="true" hidden>✦</span>
          ) : null}
          {character.isDiscovered && character.kind === 'player' && characterClass ? (
            <span className="achievements__character-class-badge">
              {characterClassLabel(characterClass)}
            </span>
          ) : null}
        </div>
        <div className="achievements__character-identity">
          <strong className="achievements__character-name">
            {character.isDiscovered ? character.name : '???'}
          </strong>
          {character.isDiscovered && (
            <ActorStats
              className="achievements__character-stats"
              stats={{
                hp: character.maxHp,
                energyPerTurn: character.energyPerTurn ?? 0,
                drawPerTurn: character.drawPerTurn ?? 0,
                attack: character.attack ?? 0,
                defense: character.defense ?? 0,
              }}
            />
          )}
        </div>
      </div>
      {character.isDiscovered && character.kind === 'player' && (
        <div className="achievements__character-details">
          <CharacterTextSection label="特性">
            <p>{character.traitDescription}</p>
          </CharacterTextSection>
          {character.actions.map(action => (
            <CharacterTextSection
              key={`${action.category}:${action.name}`}
              label={`${action.category}：${action.name}`}
            >
              {(action.energyCost > 0 || action.comboGaugeCost > 0) && (
                <p className="achievements__character-cost">
                  {[
                    action.energyCost > 0 ? `エナジー${action.energyCost}` : null,
                    action.comboGaugeCost > 0 ? `コンボゲージ${action.comboGaugeCost}` : null,
                  ].filter(Boolean).join('／')}消費
                </p>
              )}
              <CharacterAbilityDescription text={action.description} />
            </CharacterTextSection>
          ))}
          <CharacterTextSection label="キャラクター説明">
            <p>{character.description}</p>
          </CharacterTextSection>
        </div>
      )}
      {character.isDiscovered && character.kind === 'ally' && (
        <div className="achievements__character-details">
          <CharacterTextSection label="毎ターンの行動内容">
            {character.actions.map(action => (
              <div key={action.name} className="achievements__character-action">
                {character.actions.length > 1 && <strong>{action.name}</strong>}
                <CharacterAbilityDescription text={action.description} />
              </div>
            ))}
          </CharacterTextSection>
          <CharacterTextSection label="召喚方法">
            <div className="achievements__summon-cards">
              {character.summonCards.map(card => (
                <Card
                  key={card.id}
                  name={card.name}
                  cost={card.cost ?? 'X'}
                  type={cardTypeCode(card.cardType)}
                  rarity={rarityCodeFromNumber(card.rarity)}
                  poolClass={card.poolClass ?? null}
                  description={card.description}
                  upgradedDescription={card.upgradedDescription}
                />
              ))}
            </div>
          </CharacterTextSection>
        </div>
      )}
    </div>
  )
}

function CharacterTextSection({ label, children }: {
  label: string
  children: ReactNode
}) {
  return (
    <section className="achievements__character-section">
      <h3>{label}</h3>
      <div className="achievements__character-section-body">{children}</div>
    </section>
  )
}

function CharacterAbilityDescription({ text }: { text: string }) {
  return <CardDesc text={text} compress={false} renderTerm={renderCharacterTerm} />
}

function CharacterTermTooltip({ term, children }: {
  term: CardDescTerm
  children: ReactNode
}) {
  const tip = useTooltipTarget({
    name: term.name,
    desc: term.desc,
    selfReference: { kind: term.kind, id: term.id },
  })
  return (
    <span
      className="achievements__character-term"
      tabIndex={0}
      data-term={term.id}
      {...tip}
    >
      {children}
    </span>
  )
}

function renderCharacterTerm(term: CardDescTerm, rendered: ReactNode, key: number): ReactNode {
  return (
    <CharacterTermTooltip term={term} key={`${term.kind}:${term.id}:${key}`}>
      {rendered}
    </CharacterTermTooltip>
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

function characterClassLabel(value: CharacterClass): string {
  return CHARACTER_CLASS_FILTERS.find(option => option.value === value)?.label ?? value
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
  const { catalog: enemyCatalog } = useEnemyCatalog()
  const [query, setQuery] = useState('')
  const [discoveryFilter, setDiscoveryFilter] = useState<DiscoveryFilter>('all')

  const items = useMemo(() => allIds.map(id => {
    const isDiscovered = discovered.has(id)
    const displayName =
      kind === 'relic' ? (relicNames[id] ?? id)
      : kind === 'potion' ? (potionNames[id] ?? id)
      : (enemyCatalog?.[id]?.name ?? id)
    const description =
      kind === 'relic' ? (relicCatalog?.[id]?.description ?? null)
      : kind === 'potion' ? (potionCatalog?.[id]?.description ?? null)
      : enemyCatalog?.[id]
        ? formatActorStats({
          hp: enemyCatalog[id].hp,
          energyPerTurn: enemyCatalog[id].energyPerTurn ?? 0,
          drawPerTurn: enemyCatalog[id].drawPerTurn ?? 0,
          attack: enemyCatalog[id].attack ?? 0,
          defense: enemyCatalog[id].defense ?? 0,
        })
        : null
    const imageSrc = kind === 'enemy' && enemyCatalog?.[id]?.imageId
      ? `/characters/enemies/${enemyCatalog[id].imageId}.png`
      : null
    const rarity =
      kind === 'relic' ? rarityCodeFromName(relicCatalog?.[id]?.rarity)
      : kind === 'potion' ? optionalRarityCodeFromNumber(potionCatalog?.[id]?.rarity)
      : undefined
    const relicTooltip = kind === 'relic' && isDiscovered
      ? getRelicTooltipContent(id, relicCatalog, relicNames)
      : null
    return { id, isDiscovered, displayName, description, rarity, relicTooltip, imageSrc }
  }), [allIds, discovered, enemyCatalog, kind, relicCatalog, relicNames, potionCatalog, potionNames])

  const normalizedQuery = query.trim().toLocaleLowerCase('ja')
  const filteredItems = items.filter(item => {
    const matchesDiscovery = discoveryFilter === 'all'
      || (discoveryFilter === 'discovered' ? item.isDiscovered : !item.isDiscovered)
    const matchesQuery = normalizedQuery.length === 0
      || (item.isDiscovered && item.displayName.toLocaleLowerCase('ja').includes(normalizedQuery))
    return matchesDiscovery && matchesQuery
  })

  // Enemies have no rarity — render flat. Relics/potions group by rarity.
  const flat = kind === 'enemy'

  return (
    <div className={`achievements__panel achievements__panel--${kind}`}>
      <div className="achievements__panel-title-row">
        <div className="achievements__panel-title">▸ {title.toUpperCase()}</div>
        <p className="achievements__count">{discovered.size} / {allIds.length} 発見</p>
      </div>
      <ArchiveTools
        label={title}
        query={query}
        onQuery={setQuery}
        discovery={discoveryFilter}
        onDiscovery={setDiscoveryFilter}
      />
      <div className="achievements__scroll">
        {filteredItems.length === 0 && <p className="achievements__empty">該当する項目はありません。</p>}
        {flat ? (
          <div className="achievements__tile-grid">
            {filteredItems.map(it => (
              <BestiaryTile
                key={it.id}
                kind={kind}
                id={it.id}
                isDiscovered={it.isDiscovered}
                displayName={it.displayName}
                description={it.description}
                rarity={it.rarity}
                tooltipContent={it.relicTooltip}
                imageSrc={it.imageSrc}
              />
            ))}
          </div>
        ) : (
          RARITY_ORDER.map(rarity => {
            const list = filteredItems.filter(it => it.rarity === rarity)
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
                      tooltipContent={it.relicTooltip}
                      imageSrc={it.imageSrc}
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

function BestiaryTile({ kind, id, isDiscovered, displayName, description, rarity, tooltipContent, imageSrc }: {
  kind: TileKind
  id: string
  isDiscovered: boolean
  displayName: string
  description: string | null
  rarity?: CardRarity
  tooltipContent?: TooltipContent | null
  imageSrc?: string | null
}) {
  const fallbackTooltip = useMemo<TooltipContent | null>(() => {
    if (!isDiscovered) return null
    return { name: displayName, rarity, desc: description ?? '—' }
  }, [isDiscovered, displayName, description, rarity])
  const tip = useTooltipTarget(isDiscovered ? (tooltipContent ?? fallbackTooltip) : null)

  const art = (() => {
    if (kind === 'relic') return <img src={`/icons/relics/${id}.png`} alt="" draggable={false} />
    if (kind === 'potion') return <img src={`/icons/potions/${id}.png`} alt="" draggable={false} />
    if (isDiscovered && imageSrc) return (
      <>
        <img
          src={imageSrc}
          alt=""
          draggable={false}
          onError={event => {
            event.currentTarget.hidden = true
            event.currentTarget.nextElementSibling?.removeAttribute('hidden')
          }}
        />
        <span aria-hidden="true" hidden>✦</span>
      </>
    )
    return <span aria-hidden="true">{isDiscovered ? '✦' : '?'}</span>
  })()

  const tileClasses = [
    'achievements__tile',
    `achievements__tile--${kind}`,
    isDiscovered && rarity ? `achievements__tile--rarity-${rarity}` : null,
  ].filter(Boolean).join(' ')

  return (
    <div
      className={'achievements__tile-cell' + (isDiscovered ? '' : ' is-locked')}
      tabIndex={isDiscovered ? 0 : -1}
      role={isDiscovered ? 'button' : 'img'}
      aria-label={isDiscovered ? `${displayName}の詳細` : '未発見'}
      {...tip}
      onClick={event => { if (isDiscovered) event.currentTarget.focus() }}
      onKeyDown={event => {
        if (!isDiscovered || (event.key !== 'Enter' && event.key !== ' ')) return
        event.preventDefault()
        event.currentTarget.focus()
      }}
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
          <span>???</span>
        )}
      </div>
    </div>
  )
}

function ArchiveTools({ label, query, onQuery, discovery, onDiscovery }: {
  label: string
  query: string
  onQuery: (value: string) => void
  discovery: DiscoveryFilter
  onDiscovery: (value: DiscoveryFilter) => void
}) {
  return (
    <div className="achievements__tools">
      <label className="achievements__search">
        <span aria-hidden="true">⌕</span>
        <input
          type="search"
          value={query}
          onChange={event => onQuery(event.currentTarget.value)}
          placeholder={`${label}を検索`}
          aria-label={`${label}を検索`}
        />
      </label>
      <div className="achievements__discovery-filter" role="group" aria-label="発見状態で絞り込み">
        {([
          ['all', 'すべて'],
          ['discovered', '発見済み'],
          ['undiscovered', '未発見'],
        ] as const).map(([value, text]) => (
          <button
            key={value}
            type="button"
            aria-pressed={discovery === value}
            onClick={() => onDiscovery(value)}
          >
            {text}
          </button>
        ))}
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
